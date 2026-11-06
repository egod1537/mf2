using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MF2.Minecraft.Editor
{
    public enum MinecraftTexturePackId
    {
        MinecraftDefault = 0,
        Rodrigo = 1
    }

    public static class MinecraftTexturePackCatalog
    {
        public const string RodrigoDownloadUrl =
            "https://cdn.modrinth.com/data/e2vOvRqb/versions/vnwWP7cq/" +
            "Rodrigo%27s%20Pack%20%281.16%29.zip";
        public const string RodrigoExpectedSha512 =
            "04d63e2df6f2e05abe02b8c839957c60fb348a4fc92d68e4b1ba04e8a63378ac" +
            "e90d3bcd00e4386ea0b0cd8ea0fb89f833a3066a9a4feddc23a6338fed0409d3";
        public const string RodrigoRelativeZipPath =
            "LocalTexturePacks/Rodrigo/RodrigosPack-1.16.zip";

        private const string ActivePackEditorPrefsKey =
            "MF2.Minecraft.TexturePack.ActivePack.v1";

        public static MinecraftTexturePackId ActivePack
        {
            get
            {
                var value = EditorPrefs.GetInt(
                    ActivePackEditorPrefsKey,
                    (int)MinecraftTexturePackId.MinecraftDefault);
                return Enum.IsDefined(typeof(MinecraftTexturePackId), value)
                    ? (MinecraftTexturePackId)value
                    : MinecraftTexturePackId.MinecraftDefault;
            }
        }

        public static string RodrigoZipPath => ToAbsoluteProjectPath(RodrigoRelativeZipPath);

        public static string DisplayName(MinecraftTexturePackId packId)
        {
            switch (packId)
            {
                case MinecraftTexturePackId.Rodrigo:
                    return "Rodrigo's Pack (1.16, 8x8)";
                default:
                    return "Minecraft Default (26.3, 16x16)";
            }
        }

        public static bool IsInstalled(MinecraftTexturePackId packId)
        {
            return packId == MinecraftTexturePackId.MinecraftDefault ||
                   File.Exists(RodrigoZipPath);
        }

        public static bool ValidateInstalledPack(
            MinecraftTexturePackId packId,
            out string message)
        {
            if (packId == MinecraftTexturePackId.MinecraftDefault)
            {
                message = "Built-in Minecraft 26.3 source textures are available.";
                return true;
            }

            if (!File.Exists(RodrigoZipPath))
            {
                message = "Rodrigo's Pack has not been downloaded.";
                return false;
            }

            var actualHash = ComputeSha512(RodrigoZipPath);
            if (!string.Equals(actualHash, RodrigoExpectedSha512, StringComparison.Ordinal))
            {
                message = "Rodrigo ZIP SHA-512 does not match the official Modrinth file.";
                return false;
            }

            try
            {
                using (var archive = ZipFile.OpenRead(RodrigoZipPath))
                {
                    var count = archive.Entries.Count(entry =>
                        entry.FullName.StartsWith(
                            "assets/minecraft/textures/block/",
                            StringComparison.OrdinalIgnoreCase) &&
                        entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
                    if (count == 0)
                    {
                        message = "Rodrigo ZIP contains no Minecraft block textures.";
                        return false;
                    }

                    message = $"Official Rodrigo ZIP verified ({count} block textures).";
                    return true;
                }
            }
            catch (InvalidDataException exception)
            {
                message = "Rodrigo ZIP is unreadable: " + exception.Message;
                return false;
            }
        }

        public static void DownloadRodrigo()
        {
            var targetPath = RodrigoZipPath;
            var directory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("Rodrigo download folder could not be resolved.");

            Directory.CreateDirectory(directory);
            var temporaryPath = targetPath + ".download";
            try
            {
                byte[] bytes;
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(
                        "User-Agent",
                        "mf2-texture-pack-editor/1.0 (egod1537/mf2)");
                    bytes = client.GetByteArrayAsync(RodrigoDownloadUrl)
                        .GetAwaiter()
                        .GetResult();
                }

                var actualHash = ComputeSha512(bytes);
                if (!string.Equals(actualHash, RodrigoExpectedSha512, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Downloaded Rodrigo ZIP SHA-512 does not match the official file.");
                }

                File.WriteAllBytes(temporaryPath, bytes);
                File.Copy(temporaryPath, targetPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        internal static void SetActivePack(MinecraftTexturePackId packId)
        {
            EditorPrefs.SetInt(ActivePackEditorPrefsKey, (int)packId);
        }

        internal static string ComputeSha512(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA512.Create())
                return ToHex(hash.ComputeHash(stream));
        }

        internal static string ComputeSha512(byte[] bytes)
        {
            using (var hash = SHA512.Create())
                return ToHex(hash.ComputeHash(bytes));
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
                builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static string ToAbsoluteProjectPath(string relativePath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    internal sealed class MinecraftTexturePackSource : IDisposable
    {
        private const string TextureIdPrefix = "minecraft:block/";
        private const string ArchiveTexturePrefix = "assets/minecraft/textures/block/";

        private readonly MinecraftTexturePackId packId;
        private readonly ZipArchive archive;
        private readonly Dictionary<string, ZipArchiveEntry> archiveEntries;
        private readonly Dictionary<string, CachedTexture> cache =
            new Dictionary<string, CachedTexture>(StringComparer.Ordinal);
        private readonly HashSet<string> overrides =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> fallbacks =
            new HashSet<string>(StringComparer.Ordinal);

        private bool disposed;

        private MinecraftTexturePackSource(
            MinecraftTexturePackId packId,
            string archivePath,
            bool validateOfficialHash)
        {
            this.packId = packId;
            if (packId == MinecraftTexturePackId.MinecraftDefault)
                return;

            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("Texture pack ZIP was not found.", archivePath);
            if (validateOfficialHash &&
                !string.Equals(
                    MinecraftTexturePackCatalog.ComputeSha512(archivePath),
                    MinecraftTexturePackCatalog.RodrigoExpectedSha512,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Rodrigo ZIP SHA-512 does not match the official Modrinth file.");
            }

            archive = ZipFile.OpenRead(archivePath);
            archiveEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .GroupBy(entry => NormalizeArchivePath(entry.FullName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        public MinecraftTexturePackId PackId => packId;
        public int OverrideTextureCount => overrides.Count;
        public int FallbackTextureCount => fallbacks.Count;

        public static MinecraftTexturePackSource Open(MinecraftTexturePackId packId)
        {
            switch (packId)
            {
                case MinecraftTexturePackId.Rodrigo:
                    return new MinecraftTexturePackSource(
                        packId,
                        MinecraftTexturePackCatalog.RodrigoZipPath,
                        true);
                case MinecraftTexturePackId.MinecraftDefault:
                    return new MinecraftTexturePackSource(packId, null, false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(packId), packId, null);
            }
        }

        internal static MinecraftTexturePackSource OpenArchiveForTests(string archivePath)
        {
            return new MinecraftTexturePackSource(
                MinecraftTexturePackId.Rodrigo,
                archivePath,
                false);
        }

        public byte[] ReadTextureBytes(string textureId)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(textureId) ||
                !textureId.StartsWith(TextureIdPrefix, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Unsupported Minecraft block texture ID: " + textureId,
                    nameof(textureId));
            }

            if (cache.TryGetValue(textureId, out var cached))
                return cached.Bytes;

            var relativeName = textureId.Substring(TextureIdPrefix.Length);
            if (archiveEntries != null)
            {
                var archivePath = ArchiveTexturePrefix + relativeName + ".png";
                if (archiveEntries.TryGetValue(archivePath, out var entry))
                {
                    var bytes = ReadAllBytes(entry);
                    cache[textureId] = new CachedTexture(bytes);
                    overrides.Add(textureId);
                    return bytes;
                }
                fallbacks.Add(textureId);
            }

            var defaultPath = Path.Combine(
                Application.dataPath,
                "Minecraft",
                "Textures",
                "Block",
                relativeName.Replace('/', Path.DirectorySeparatorChar) + ".png");
            if (!File.Exists(defaultPath))
                throw new FileNotFoundException("Minecraft source texture is missing.", defaultPath);

            var defaultBytes = File.ReadAllBytes(defaultPath);
            cache[textureId] = new CachedTexture(defaultBytes);
            return defaultBytes;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            archive?.Dispose();
            cache.Clear();
            overrides.Clear();
            fallbacks.Clear();
        }

        private static byte[] ReadAllBytes(ZipArchiveEntry entry)
        {
            using (var input = entry.Open())
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private static string NormalizeArchivePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(MinecraftTexturePackSource));
        }

        private readonly struct CachedTexture
        {
            public CachedTexture(byte[] bytes)
            {
                Bytes = bytes;
            }

            public byte[] Bytes { get; }
        }
    }
}
