using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace MF2.Game.Persistence
{
    public interface ISessionArchiveStorage
    {
        void Save(SessionArchiveSnapshot snapshot);
        SessionArchiveSnapshot LoadCurrent();
        SessionArchiveSnapshot LoadBackup();
    }

    public sealed class SessionStorageException : IOException
    {
        public SessionStorageException(string message)
            : base(message)
        {
        }

        public SessionStorageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Crash-safe session snapshots. Region JSON files are written into a new immutable
    /// generation first; current.json is the final, atomic commit marker.
    /// </summary>
    public sealed class SessionJsonStorage : ISessionArchiveStorage
    {
        public const int FormatVersion = 2;
        public const string CurrentPointerFileName = "current.json";
        public const string BackupSuffix = ".bak";
        public const string TemporarySuffix = ".tmp";

        private const string SnapshotsFolderName = "snapshots";
        private const string RegionsFolderName = "regions";
        private const string ManifestFileName = "session.json";
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Culture = CultureInfo.InvariantCulture,
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include
        };

        private readonly string rootDirectory;

        public SessionJsonStorage(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A session storage directory is required.", nameof(rootDirectory));
            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public string RootDirectory => rootDirectory;
        public string CurrentPointerPath => Path.Combine(rootDirectory, CurrentPointerFileName);
        public string CurrentPointerBackupPath => CurrentPointerPath + BackupSuffix;
        public string SnapshotsDirectory => Path.Combine(rootDirectory, SnapshotsFolderName);

        public void Save(SessionArchiveSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var generation = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            var stageDirectory = GetGenerationDirectory(generation + TemporarySuffix);
            var committedDirectory = GetGenerationDirectory(generation);
            try
            {
                Directory.CreateDirectory(Path.Combine(stageDirectory, RegionsFolderName));
                var regionsDirectory = Path.Combine(stageDirectory, RegionsFolderName);
                SaveWorld(snapshot.World, regionsDirectory);

                var manifest = ManifestDto.From(snapshot, ComputeWorldChecksum(regionsDirectory));
                var manifestPath = Path.Combine(stageDirectory, ManifestFileName);
                WriteNewFile(
                    manifestPath,
                    JsonConvert.SerializeObject(manifest, JsonSettings));
                var manifestChecksum = ComputeFileChecksum(manifestPath);

                Directory.Move(stageDirectory, committedDirectory);
                Directory.CreateDirectory(rootDirectory);
                WriteAtomically(
                    CurrentPointerPath,
                    JsonConvert.SerializeObject(new PointerDto
                    {
                        FormatVersion = FormatVersion,
                        Generation = generation,
                        ManifestChecksum = manifestChecksum
                    }, JsonSettings));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is CryptographicException ||
                exception is ArgumentException)
            {
                if (Directory.Exists(stageDirectory))
                    Directory.Delete(stageDirectory, true);
                throw new SessionStorageException("Failed to commit the session snapshot.", exception);
            }
        }

        public SessionArchiveSnapshot LoadCurrent() => LoadFromPointer(CurrentPointerPath);

        /// <summary>Loads the previous pointer explicitly; backups are never promoted implicitly.</summary>
        public SessionArchiveSnapshot LoadBackup() => LoadFromPointer(CurrentPointerBackupPath);

        private SessionArchiveSnapshot LoadFromPointer(string pointerPath)
        {
            if (!File.Exists(pointerPath))
                throw new SessionStorageException($"Session pointer does not exist: '{pointerPath}'.");

            try
            {
                var pointer = Deserialize<PointerDto>(pointerPath);
                ValidatePointer(pointer, pointerPath);
                var generationDirectory = GetGenerationDirectory(pointer.Generation);
                var manifestPath = Path.Combine(generationDirectory, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    throw new SessionStorageException(
                        $"Committed session manifest is missing: '{manifestPath}'.");
                }
                var manifest = Deserialize<ManifestDto>(manifestPath);
                ValidateManifest(manifest, manifestPath);
                var actualManifestChecksum = ComputeFileChecksum(manifestPath);
                if (!string.Equals(
                        actualManifestChecksum,
                        pointer.ManifestChecksum,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SessionStorageException(
                        $"Committed session manifest checksum does not match generation " +
                        $"{pointer.Generation}.");
                }
                var regionsDirectory = Path.Combine(generationDirectory, RegionsFolderName);
                if (!Directory.Exists(regionsDirectory))
                {
                    throw new SessionStorageException(
                        $"Committed region directory is missing: '{regionsDirectory}'.");
                }
                var actualWorldChecksum = ComputeWorldChecksum(regionsDirectory);
                if (!string.Equals(
                        actualWorldChecksum,
                        manifest.WorldChecksum,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new SessionStorageException(
                        $"Committed region checksum does not match generation {pointer.Generation}.");
                }
                var world = new RegionJsonWorldStorage(regionsDirectory).LoadAll();
                if (world.ChunkCount != manifest.WorldChunkCount)
                {
                    throw new SessionStorageException(
                        $"Manifest expected {manifest.WorldChunkCount} chunks but loaded " +
                        $"{world.ChunkCount} from generation {pointer.Generation}.");
                }

                var players = new PlayerModel[manifest.Players.Length];
                for (var i = 0; i < players.Length; i++)
                {
                    var source = manifest.Players[i] ??
                                 throw new SessionStorageException("Player metadata cannot be null.");
                    if (!Guid.TryParseExact(source.Id, "D", out var playerGuid) ||
                        playerGuid == Guid.Empty ||
                        source.LifeState < 0 ||
                        source.LifeState > (int)PlayerLifeState.Dead)
                    {
                        throw new SessionStorageException("Player metadata is invalid.");
                    }

                    players[i] = new PlayerModel(
                        new PlayerId(playerGuid),
                        source.Health,
                        source.MaximumHealth,
                        (PlayerLifeState)source.LifeState,
                        source.Revision);
                }

                return new SessionArchiveSnapshot(
                    new SessionId(Guid.ParseExact(manifest.SessionId, "D")),
                    manifest.Generation,
                    (SessionPhase)manifest.Phase,
                    manifest.TickIndex,
                    manifest.ElapsedSeconds,
                    manifest.Seed,
                    players,
                    world);
            }
            catch (SessionStorageException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException ||
                exception is ArgumentException ||
                exception is CryptographicException ||
                exception is OverflowException)
            {
                throw new SessionStorageException(
                    $"Failed to load committed session pointer '{pointerPath}'.",
                    exception);
            }
        }

        private static void SaveWorld(WorldStorageSnapshot snapshot, string regionDirectory)
        {
            var byRegion = new Dictionary<int3, List<StoredChunk>>();
            for (var i = 0; i < snapshot.Chunks.Count; i++)
            {
                var chunk = snapshot.Chunks[i];
                var region = RegionCoordinates.ToRegion(chunk.ChunkCoordinate);
                if (!byRegion.TryGetValue(region, out var chunks))
                {
                    chunks = new List<StoredChunk>();
                    byRegion.Add(region, chunks);
                }
                chunks.Add(chunk);
            }

            var storage = new RegionJsonWorldStorage(regionDirectory);
            foreach (var pair in byRegion)
                storage.SaveRegion(pair.Key, pair.Value);
        }

        private string GetGenerationDirectory(string generation)
        {
            var path = Path.GetFullPath(Path.Combine(SnapshotsDirectory, generation));
            var prefix = Path.GetFullPath(SnapshotsDirectory) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new SessionStorageException("Session generation escaped the snapshots directory.");
            return path;
        }

        private static void ValidatePointer(PointerDto pointer, string path)
        {
            if (pointer == null)
                throw new SessionStorageException($"Session pointer is empty: '{path}'.");
            if (pointer.FormatVersion != FormatVersion)
            {
                throw new SessionStorageException(
                    $"Unsupported session pointer version {pointer.FormatVersion}; " +
                    $"expected {FormatVersion}.");
            }
            if (!Guid.TryParseExact(pointer.Generation, "N", out _))
                throw new SessionStorageException("Session pointer generation is invalid.");
            if (!IsSha256(pointer.ManifestChecksum))
                throw new SessionStorageException("Session pointer manifest checksum is invalid.");
        }

        private static void ValidateManifest(ManifestDto manifest, string path)
        {
            if (manifest == null)
                throw new SessionStorageException($"Session manifest is empty: '{path}'.");
            if (manifest.SchemaVersion != FormatVersion)
            {
                throw new SessionStorageException(
                    $"Unsupported session manifest version {manifest.SchemaVersion}; " +
                    $"expected {FormatVersion}.");
            }
            if (!Guid.TryParseExact(manifest.SessionId, "D", out var id) || id == Guid.Empty)
                throw new SessionStorageException("Session manifest id is invalid.");
            if (manifest.Phase != (int)SessionPhase.Ready &&
                manifest.Phase != (int)SessionPhase.Running &&
                manifest.Phase != (int)SessionPhase.Paused)
            {
                throw new SessionStorageException("Session manifest phase is not restorable.");
            }
            if (manifest.Generation <= 0 ||
                manifest.TickIndex < 0 ||
                manifest.ElapsedSeconds < 0d ||
                double.IsNaN(manifest.ElapsedSeconds) ||
                double.IsInfinity(manifest.ElapsedSeconds) ||
                manifest.WorldChunkCount < 0 ||
                manifest.Players == null)
            {
                throw new SessionStorageException("Session manifest counters are invalid.");
            }
            if (!IsSha256(manifest.WorldChecksum))
                throw new SessionStorageException("Session manifest world checksum is invalid.");
        }

        private static string ComputeWorldChecksum(string regionDirectory)
        {
            if (!Directory.Exists(regionDirectory))
                throw new SessionStorageException($"Region directory does not exist: '{regionDirectory}'.");

            var files = Directory.GetFiles(
                regionDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly);
            Array.Sort(files, (left, right) => string.Compare(
                Path.GetFileName(left),
                Path.GetFileName(right),
                StringComparison.Ordinal));

            var index = new StringBuilder();
            for (var i = 0; i < files.Length; i++)
            {
                index.Append(Path.GetFileName(files[i]));
                index.Append(':');
                index.Append(ComputeFileChecksum(files[i]));
                index.Append('\n');
            }
            return ComputeBytesChecksum(Utf8WithoutBom.GetBytes(index.ToString()));
        }

        private static string ComputeFileChecksum(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            using (var algorithm = SHA256.Create())
            {
                return ToHex(algorithm.ComputeHash(stream));
            }
        }

        private static string ComputeBytesChecksum(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return ToHex(algorithm.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                result[i * 2] = digits[bytes[i] >> 4];
                result[i * 2 + 1] = digits[bytes[i] & 0x0f];
            }
            return new string(result);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f') &&
                    (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }
            return true;
        }

        private static T Deserialize<T>(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            using (var reader = new StreamReader(stream, Utf8WithoutBom, true))
            using (var json = new JsonTextReader(reader))
            {
                var serializer = JsonSerializer.Create(JsonSettings);
                var result = serializer.Deserialize<T>(json);
                if (json.Read())
                    throw new JsonSerializationException("Additional JSON content follows the root object.");
                return result;
            }
        }

        private static void WriteNewFile(string path, string content)
        {
            var temporaryPath = path + TemporarySuffix;
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temporaryPath, path);
        }

        private static void WriteAtomically(string path, string content)
        {
            var temporaryPath = path + TemporarySuffix;
            var backupPath = path + BackupSuffix;
            if (File.Exists(temporaryPath))
                throw new SessionStorageException($"Unresolved temporary pointer exists: '{temporaryPath}'.");

            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Replace(temporaryPath, path, backupPath, true);
        }

        private sealed class PointerDto
        {
            public int FormatVersion;
            public string Generation;
            public string ManifestChecksum;
        }

        private sealed class ManifestDto
        {
            public int SchemaVersion;
            public string SessionId;
            public long Generation;
            public int Phase;
            public long TickIndex;
            public double ElapsedSeconds;
            public int Seed;
            public int WorldChunkCount;
            public string WorldChecksum;
            public PlayerDto[] Players;

            public static ManifestDto From(
                SessionArchiveSnapshot snapshot,
                string worldChecksum)
            {
                var players = new PlayerDto[snapshot.Players.Count];
                for (var i = 0; i < players.Length; i++)
                {
                    var source = snapshot.Players[i];
                    players[i] = new PlayerDto
                    {
                        Id = source.Id.Value.ToString("D"),
                        Health = source.Health,
                        MaximumHealth = source.MaximumHealth,
                        LifeState = (int)source.LifeState,
                        Revision = source.Revision
                    };
                }

                return new ManifestDto
                {
                    SchemaVersion = SessionJsonStorage.FormatVersion,
                    SessionId = snapshot.Id.Value.ToString("D"),
                    Generation = snapshot.Generation,
                    Phase = (int)snapshot.Phase,
                    TickIndex = snapshot.TickIndex,
                    ElapsedSeconds = snapshot.ElapsedSeconds,
                    Seed = snapshot.Seed,
                    WorldChunkCount = snapshot.World.ChunkCount,
                    WorldChecksum = worldChecksum,
                    Players = players
                };
            }
        }

        private sealed class PlayerDto
        {
            public string Id;
            public int Health;
            public int MaximumHealth;
            public int LifeState;
            public long Revision;
        }
    }

    public sealed class SessionPersistenceCoordinator
    {
        private readonly ServerSessionCoordinator sessions;

        public SessionPersistenceCoordinator(ServerSessionCoordinator sessions)
        {
            this.sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        public SessionArchiveSnapshot SaveCurrent(ISessionArchiveStorage storage)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            var session = sessions.Session ??
                          throw new InvalidOperationException("There is no server session to save.");
            var snapshot = session.CaptureArchive();
            storage.Save(snapshot);
            return snapshot;
        }

        public GameSession RestoreCurrent(ISessionArchiveStorage storage)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            return sessions.RestoreServerSession(storage.LoadCurrent());
        }

        public GameSession RestoreBackup(ISessionArchiveStorage storage)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            return sessions.RestoreServerSession(storage.LoadBackup());
        }
    }
}
