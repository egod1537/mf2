using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MF2.World.Core;
using MF2.World.Registry;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace MF2.World.Storage
{
    /// <summary>
    /// Human-readable sparse region storage. Disk I/O and JSON allocation are intentionally
    /// managed and synchronous; callers must invoke this from explicit save points, never Repaint.
    /// </summary>
    public sealed class RegionJsonWorldStorage : IWorldStorage
    {
        public const int FormatVersion = 1;
        public const string TemporarySuffix = ".tmp";
        public const string BackupSuffix = ".bak";

        private static readonly object LockMapGate = new object();
        private static readonly Dictionary<string, object> RegionLocks =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        private readonly string rootDirectory;
        private readonly IStoredBlockValidator blockValidator;
        private RecoveryArtifact[] recoveryArtifacts;

        public RegionJsonWorldStorage(
            string rootDirectory,
            IStoredBlockValidator blockValidator = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A storage directory is required.", nameof(rootDirectory));

            this.rootDirectory = Path.GetFullPath(rootDirectory);
            this.blockValidator = blockValidator ?? StoredBlockValidator.AllowAnyNonAirId;
            recoveryArtifacts = ScanRecoveryArtifacts();
        }

        public string RootDirectory => rootDirectory;
        public IReadOnlyList<RecoveryArtifact> RecoveryArtifacts => recoveryArtifacts;

        public WorldStorageSnapshot LoadAll()
        {
            RefreshRecoveryArtifacts();
            ThrowForOrphanedBackups(recoveryArtifacts);
            if (!Directory.Exists(rootDirectory))
                return new WorldStorageSnapshot(Array.Empty<StoredChunk>());

            var regionFiles = new List<RegionFilePath>();
            var seenRegions = new HashSet<int3>();
            var files = Directory.GetFiles(rootDirectory, "*.json", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                var fileName = Path.GetFileName(files[i]);
                if (!fileName.StartsWith("r.", StringComparison.Ordinal))
                    continue;
                if (!RegionCoordinates.TryParseFileName(fileName, out var regionCoordinate))
                {
                    throw new WorldStorageException(
                        $"Region file name '{fileName}' does not match r.<x>.<y>.<z>.json.");
                }
                if (!seenRegions.Add(regionCoordinate))
                {
                    throw new WorldStorageException(
                        $"More than one file name resolves to region {regionCoordinate}.");
                }

                regionFiles.Add(new RegionFilePath(regionCoordinate, files[i]));
            }

            regionFiles.Sort((left, right) => CompareCoordinates(
                left.RegionCoordinate,
                right.RegionCoordinate));

            var chunks = new List<StoredChunk>();
            var seenChunks = new HashSet<int3>();
            for (var i = 0; i < regionFiles.Count; i++)
            {
                var loaded = ReadRegionFile(regionFiles[i].RegionCoordinate, regionFiles[i].Path);
                for (var chunkIndex = 0; chunkIndex < loaded.Count; chunkIndex++)
                {
                    var chunk = loaded[chunkIndex];
                    if (!seenChunks.Add(chunk.ChunkCoordinate))
                    {
                        throw new WorldStorageException(
                            $"Chunk {chunk.ChunkCoordinate} appears in more than one region file.");
                    }

                    chunks.Add(chunk);
                }
            }

            return new WorldStorageSnapshot(chunks);
        }

        public IReadOnlyList<StoredChunk> LoadRegion(int3 regionCoordinate)
        {
            var mainPath = GetRegionPath(regionCoordinate);
            var backupPath = mainPath + BackupSuffix;
            if (!File.Exists(mainPath) && File.Exists(backupPath))
            {
                throw new WorldStorageException(
                    $"Region {regionCoordinate} has no primary file but backup '{backupPath}' exists. " +
                    "Recovery must be an explicit user decision; backups are never promoted automatically.");
            }

            if (!File.Exists(mainPath))
                return Array.Empty<StoredChunk>();

            return ReadRegionFile(regionCoordinate, mainPath);
        }

        public void SaveRegion(int3 regionCoordinate, IReadOnlyList<StoredChunk> replacementChunks)
        {
            if (replacementChunks == null)
                throw new ArgumentNullException(nameof(replacementChunks));

            var mainPath = GetRegionPath(regionCoordinate);
            var regionLock = GetRegionLock(mainPath);
            lock (regionLock)
            {
                Directory.CreateDirectory(rootDirectory);
                using (AcquireWriterLease(mainPath))
                {
                    EnsureNoRecoveryArtifactForWrite(mainPath);
                    var merged = new Dictionary<int3, StoredChunk>();
                    var existing = LoadRegion(regionCoordinate);
                    for (var i = 0; i < existing.Count; i++)
                        merged.Add(existing[i].ChunkCoordinate, existing[i]);

                    var seenReplacements = new HashSet<int3>();
                    for (var i = 0; i < replacementChunks.Count; i++)
                    {
                        var chunk = replacementChunks[i] ??
                                    throw new ArgumentException("Replacement chunks cannot contain null.");
                        if (!seenReplacements.Add(chunk.ChunkCoordinate))
                        {
                            throw new WorldStorageException(
                                $"Save request contains duplicate chunk {chunk.ChunkCoordinate}.");
                        }

                        var actualRegion = RegionCoordinates.ToRegion(chunk.ChunkCoordinate);
                        if (!actualRegion.Equals(regionCoordinate))
                        {
                            throw new WorldStorageException(
                                $"Chunk {chunk.ChunkCoordinate} belongs to region {actualRegion}, not " +
                                $"requested region {regionCoordinate}.");
                        }

                        ValidateChunkForSave(chunk);
                        if (chunk.IsEmpty)
                            merged.Remove(chunk.ChunkCoordinate);
                        else
                            merged[chunk.ChunkCoordinate] = chunk;
                    }

                    if (merged.Count == 0)
                    {
                        DeleteRegionFile(mainPath);
                        RefreshRecoveryArtifacts();
                        return;
                    }

                    var dto = BuildRegionDto(regionCoordinate, merged.Values);
                    var json = Serialize(dto);
                    WriteAtomically(mainPath, json);
                    RefreshRecoveryArtifacts();
                }
            }
        }

        private List<StoredChunk> ReadRegionFile(int3 expectedRegion, string path)
        {
            RegionFileDto dto;
            try
            {
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           4096,
                           FileOptions.SequentialScan))
                using (var reader = new StreamReader(stream, Utf8WithoutBom, true))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var serializer = JsonSerializer.Create(CreateSerializerSettings());
                    dto = serializer.Deserialize<RegionFileDto>(jsonReader);
                    if (jsonReader.Read())
                        throw new JsonSerializationException("Additional JSON content follows the region object.");
                }
            }
            catch (WorldStorageException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is DecoderFallbackException)
            {
                throw new WorldStorageException($"Failed to read region JSON '{path}'.", exception);
            }

            return ValidateAndConvert(dto, expectedRegion, path);
        }

        private List<StoredChunk> ValidateAndConvert(
            RegionFileDto dto,
            int3 expectedRegion,
            string path)
        {
            if (dto == null)
                throw new WorldStorageException($"Region JSON '{path}' contains null instead of an object.");
            if (dto.FormatVersion != FormatVersion)
            {
                throw new WorldStorageException(
                    $"Region JSON '{path}' uses formatVersion {dto.FormatVersion}; expected {FormatVersion}.");
            }
            if (!string.Equals(dto.HashScheme, StableBlockId.PersistenceScheme, StringComparison.Ordinal))
            {
                throw new WorldStorageException(
                    $"Region JSON '{path}' uses hashScheme '{dto.HashScheme}'; expected " +
                    $"'{StableBlockId.PersistenceScheme}'.");
            }

            var storedRegion = ReadCoordinate(dto.Region, "region", path);
            if (!storedRegion.Equals(expectedRegion))
            {
                throw new WorldStorageException(
                    $"Region JSON '{path}' declares {storedRegion}, but its expected region is " +
                    $"{expectedRegion}.");
            }
            if (dto.Chunks == null)
                throw new WorldStorageException($"Region JSON '{path}' has no chunks array.");
            if (dto.Chunks.Count == 0)
            {
                throw new WorldStorageException(
                    $"Region JSON '{path}' has no chunks; empty regions must be deleted.");
            }
            if (dto.Chunks.Count > RegionCoordinates.ChunksPerAxis *
                RegionCoordinates.ChunksPerAxis * RegionCoordinates.ChunksPerAxis)
            {
                throw new WorldStorageException($"Region JSON '{path}' contains too many chunks.");
            }

            var result = new List<StoredChunk>(dto.Chunks.Count);
            var seenLocalChunks = new HashSet<int3>();
            for (var chunkIndex = 0; chunkIndex < dto.Chunks.Count; chunkIndex++)
            {
                var chunkDto = dto.Chunks[chunkIndex];
                if (chunkDto == null)
                    throw new WorldStorageException($"Region JSON '{path}' contains a null chunk entry.");

                var localChunk = ReadCoordinate(chunkDto.Local, "chunk.local", path);
                try
                {
                    RegionCoordinates.ValidateLocalChunk(localChunk);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new WorldStorageException(
                        $"Region JSON '{path}' has out-of-range local chunk {localChunk}.",
                        exception);
                }

                if (!seenLocalChunks.Add(localChunk))
                {
                    throw new WorldStorageException(
                        $"Region JSON '{path}' contains duplicate local chunk {localChunk}.");
                }
                if (chunkDto.Blocks == null)
                    throw new WorldStorageException($"Region JSON '{path}' has a chunk without blocks.");
                if (chunkDto.Blocks.Count == 0)
                {
                    throw new WorldStorageException(
                        $"Region JSON '{path}' contains an empty chunk entry; empty chunks must be omitted.");
                }
                if (chunkDto.Blocks.Count > ChunkCoordinates.Volume)
                    throw new WorldStorageException($"Region JSON '{path}' contains too many blocks in a chunk.");

                int3 chunkCoordinate;
                try
                {
                    chunkCoordinate = RegionCoordinates.ComposeChunk(expectedRegion, localChunk);
                    RegionCoordinates.ValidateWorldChunkRange(chunkCoordinate);
                }
                catch (Exception exception) when (
                    exception is OverflowException || exception is ArgumentOutOfRangeException)
                {
                    throw new WorldStorageException(
                        $"Region JSON '{path}' composes a chunk outside signed block space.",
                        exception);
                }

                var blocks = new StoredBlock[chunkDto.Blocks.Count];
                var seenIndices = new HashSet<int>();
                for (var blockIndex = 0; blockIndex < chunkDto.Blocks.Count; blockIndex++)
                {
                    var blockDto = chunkDto.Blocks[blockIndex];
                    if (blockDto == null)
                        throw new WorldStorageException($"Region JSON '{path}' contains a null block entry.");
                    if ((uint)blockDto.Index >= ChunkCoordinates.Volume)
                    {
                        throw new WorldStorageException(
                            $"Region JSON '{path}' contains block index {blockDto.Index}; expected 0..4095.");
                    }
                    if (!seenIndices.Add(blockDto.Index))
                    {
                        throw new WorldStorageException(
                            $"Region JSON '{path}' contains duplicate block index {blockDto.Index}.");
                    }

                    var context = $"{path}, chunk {chunkCoordinate}, index {blockDto.Index}";
                    blockValidator.Validate(blockDto.Id, blockDto.State, context);
                    ValidateDebugName(blockDto.Name, blockDto.Id, context);
                    blocks[blockIndex] = new StoredBlock(
                        blockDto.Index,
                        blockDto.Id,
                        blockDto.State);
                }

                result.Add(new StoredChunk(chunkCoordinate, blocks));
            }

            result.Sort((left, right) => CompareCoordinates(
                left.ChunkCoordinate,
                right.ChunkCoordinate));
            return result;
        }

        private void ValidateChunkForSave(StoredChunk chunk)
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < chunk.Blocks.Count; i++)
            {
                var block = chunk.Blocks[i];
                if (!seen.Add(block.Index))
                {
                    throw new WorldStorageException(
                        $"Chunk {chunk.ChunkCoordinate} contains duplicate index {block.Index}.");
                }

                blockValidator.Validate(
                    block.Id,
                    block.State,
                    $"chunk {chunk.ChunkCoordinate}, index {block.Index}");
            }
        }

        private static void ValidateDebugName(string name, uint id, string context)
        {
            if (string.IsNullOrEmpty(name))
                return;

            uint namedId;
            try
            {
                namedId = StableBlockId.Compute(name);
            }
            catch (ArgumentException exception)
            {
                throw new WorldStorageException($"{context}: debug name '{name}' is not canonical.", exception);
            }

            if (namedId != id)
            {
                throw new WorldStorageException(
                    $"{context}: debug name '{name}' hashes to 0x{namedId:X8}, not stored ID 0x{id:X8}.");
            }
        }

        private static RegionFileDto BuildRegionDto(
            int3 regionCoordinate,
            ICollection<StoredChunk> chunks)
        {
            var sorted = new List<StoredChunk>(chunks);
            sorted.Sort((left, right) => CompareCoordinates(
                left.ChunkCoordinate,
                right.ChunkCoordinate));

            var chunkDtos = new List<ChunkDto>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                var chunk = sorted[i];
                var blocks = new List<BlockDto>(chunk.Blocks.Count);
                for (var blockIndex = 0; blockIndex < chunk.Blocks.Count; blockIndex++)
                {
                    var block = chunk.Blocks[blockIndex];
                    blocks.Add(new BlockDto
                    {
                        Index = block.Index,
                        Id = block.Id,
                        State = block.State
                    });
                }

                var local = RegionCoordinates.ToLocalChunk(chunk.ChunkCoordinate);
                chunkDtos.Add(new ChunkDto
                {
                    Local = new[] { local.x, local.y, local.z },
                    Blocks = blocks
                });
            }

            return new RegionFileDto
            {
                FormatVersion = FormatVersion,
                HashScheme = StableBlockId.PersistenceScheme,
                Region = new[] { regionCoordinate.x, regionCoordinate.y, regionCoordinate.z },
                Chunks = chunkDtos
            };
        }

        private static string Serialize(RegionFileDto dto)
        {
            var settings = CreateSerializerSettings();
            var serializer = JsonSerializer.Create(settings);
            var builder = new StringBuilder();
            using (var writer = new StringWriter(builder, CultureInfo.InvariantCulture)
                   {
                       NewLine = "\n"
                   })
            using (var jsonWriter = new JsonTextWriter(writer)
                   {
                       Formatting = Formatting.Indented,
                       IndentChar = ' ',
                       Indentation = 2
                   })
            {
                serializer.Serialize(jsonWriter, dto);
            }

            builder.Append('\n');
            return builder.ToString();
        }

        private static JsonSerializerSettings CreateSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None
            };
        }

        private static int3 ReadCoordinate(int[] values, string field, string path)
        {
            if (values == null || values.Length != 3)
            {
                throw new WorldStorageException(
                    $"Region JSON '{path}' field '{field}' must contain exactly three integers.");
            }

            return new int3(values[0], values[1], values[2]);
        }

        private void WriteAtomically(string mainPath, string contents)
        {
            var temporaryPath = mainPath + TemporarySuffix;
            var backupPath = mainPath + BackupSuffix;
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, Utf8WithoutBom))
                {
                    writer.Write(contents);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (!File.Exists(mainPath))
                {
                    File.Move(temporaryPath, mainPath);
                    return;
                }

                try
                {
                    File.Replace(temporaryPath, mainPath, backupPath, true);
                }
                catch (Exception exception) when (
                    exception is PlatformNotSupportedException ||
                    exception is NotSupportedException ||
                    exception is IOException)
                {
                    if (!File.Exists(temporaryPath))
                        throw;
                    ReplaceWithRollbackFallback(temporaryPath, mainPath, backupPath);
                }

                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (WorldStorageException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                throw new WorldStorageException(
                    $"Failed to atomically write region JSON '{mainPath}'. " +
                    $"Temporary/backup files are left for explicit recovery when present.",
                    exception);
            }
        }

        private static void ReplaceWithRollbackFallback(
            string temporaryPath,
            string mainPath,
            string backupPath)
        {
            File.Copy(mainPath, backupPath, false);
            try
            {
                File.Delete(mainPath);
                File.Move(temporaryPath, mainPath);
            }
            catch
            {
                if (!File.Exists(mainPath) && File.Exists(backupPath))
                    File.Move(backupPath, mainPath);
                throw;
            }
        }

        private static void DeleteRegionFile(string mainPath)
        {
            if (!File.Exists(mainPath))
                return;

            var backupPath = mainPath + BackupSuffix;
            File.Move(mainPath, backupPath);
            try
            {
                File.Delete(backupPath);
            }
            catch
            {
                if (!File.Exists(mainPath) && File.Exists(backupPath))
                    File.Move(backupPath, mainPath);
                throw;
            }
        }

        private void EnsureNoRecoveryArtifactForWrite(string mainPath)
        {
            var temporaryPath = mainPath + TemporarySuffix;
            var backupPath = mainPath + BackupSuffix;
            if (File.Exists(temporaryPath) || File.Exists(backupPath))
            {
                throw new WorldStorageException(
                    $"Region '{mainPath}' has a leftover temporary or backup file. " +
                    "Resolve it explicitly before saving; recovery artifacts are never overwritten.");
            }
        }

        private void RefreshRecoveryArtifacts()
        {
            recoveryArtifacts = ScanRecoveryArtifacts();
        }

        private RecoveryArtifact[] ScanRecoveryArtifacts()
        {
            if (!Directory.Exists(rootDirectory))
                return Array.Empty<RecoveryArtifact>();

            var result = new List<RecoveryArtifact>();
            var files = Directory.GetFiles(rootDirectory, "r.*", SearchOption.TopDirectoryOnly);
            for (var i = 0; i < files.Length; i++)
            {
                var path = files[i];
                RecoveryArtifactKind kind;
                string mainPath;
                if (path.EndsWith(".json" + TemporarySuffix, StringComparison.Ordinal))
                {
                    kind = RecoveryArtifactKind.Temporary;
                    mainPath = path.Substring(0, path.Length - TemporarySuffix.Length);
                }
                else if (path.EndsWith(".json" + BackupSuffix, StringComparison.Ordinal))
                {
                    kind = RecoveryArtifactKind.Backup;
                    mainPath = path.Substring(0, path.Length - BackupSuffix.Length);
                }
                else
                {
                    continue;
                }

                if (!RegionCoordinates.TryParseFileName(Path.GetFileName(mainPath), out _))
                    continue;
                result.Add(new RecoveryArtifact(kind, path, mainPath, File.Exists(mainPath)));
            }

            result.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return result.ToArray();
        }

        private static void ThrowForOrphanedBackups(IReadOnlyList<RecoveryArtifact> artifacts)
        {
            for (var i = 0; i < artifacts.Count; i++)
            {
                var artifact = artifacts[i];
                if (artifact.Kind == RecoveryArtifactKind.Backup && !artifact.MainFileExists)
                {
                    throw new WorldStorageException(
                        $"Backup '{artifact.Path}' exists without primary '{artifact.MainPath}'. " +
                        "Choose recovery explicitly; it will not be promoted automatically.");
                }
            }
        }

        private string GetRegionPath(int3 regionCoordinate)
        {
            return Path.Combine(rootDirectory, RegionCoordinates.GetFileName(regionCoordinate));
        }

        private static object GetRegionLock(string mainPath)
        {
            lock (LockMapGate)
            {
                if (!RegionLocks.TryGetValue(mainPath, out var regionLock))
                {
                    regionLock = new object();
                    RegionLocks.Add(mainPath, regionLock);
                }

                return regionLock;
            }
        }

        private static RegionWriterLease AcquireWriterLease(string mainPath)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                var hash = offset;
                var normalized = Path.GetFullPath(mainPath).ToUpperInvariant();
                for (var i = 0; i < normalized.Length; i++)
                {
                    hash ^= normalized[i];
                    hash *= prime;
                }

                return new RegionWriterLease("MF2_WorldRegion_" + hash.ToString("X16"));
            }
        }

        private static int CompareCoordinates(int3 left, int3 right)
        {
            var x = left.x.CompareTo(right.x);
            if (x != 0)
                return x;
            var y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.z.CompareTo(right.z);
        }

        private readonly struct RegionFilePath
        {
            public readonly int3 RegionCoordinate;
            public readonly string Path;

            public RegionFilePath(int3 regionCoordinate, string path)
            {
                RegionCoordinate = regionCoordinate;
                Path = path;
            }
        }

        private sealed class RegionWriterLease : IDisposable
        {
            private readonly Mutex mutex;
            private bool ownsMutex;

            public RegionWriterLease(string name)
            {
                mutex = new Mutex(false, name);
                try
                {
                    ownsMutex = mutex.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }

                if (!ownsMutex)
                {
                    mutex.Dispose();
                    throw new WorldStorageException(
                        "Another writer is already saving this region.");
                }
            }

            public void Dispose()
            {
                if (ownsMutex)
                {
                    mutex.ReleaseMutex();
                    ownsMutex = false;
                }

                mutex.Dispose();
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class RegionFileDto
        {
            [JsonProperty("formatVersion", Order = 0, Required = Required.Always)]
            public int FormatVersion;

            [JsonProperty("hashScheme", Order = 1, Required = Required.Always)]
            public string HashScheme;

            [JsonProperty("region", Order = 2, Required = Required.Always)]
            public int[] Region;

            [JsonProperty("chunks", Order = 3, Required = Required.Always)]
            public List<ChunkDto> Chunks;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class ChunkDto
        {
            [JsonProperty("local", Order = 0, Required = Required.Always)]
            public int[] Local;

            [JsonProperty("blocks", Order = 1, Required = Required.Always)]
            public List<BlockDto> Blocks;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class BlockDto
        {
            [JsonProperty("index", Order = 0, Required = Required.Always)]
            public int Index;

            [JsonProperty("id", Order = 1, Required = Required.Always)]
            public uint Id;

            [JsonProperty("state", Order = 2, Required = Required.Always)]
            public uint State;

            [JsonProperty("name", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
            public string Name;
        }
    }
}
