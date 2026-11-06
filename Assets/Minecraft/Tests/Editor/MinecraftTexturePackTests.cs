using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

namespace MF2.Minecraft.Editor.Tests
{
    public sealed class MinecraftTexturePackTests
    {
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "MF2-MinecraftTexturePackTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [Test]
        public void ArchiveSource_OverridesAvailableTextureAndFallsBackToMinecraftDefault()
        {
            var archivePath = Path.Combine(temporaryDirectory, "test-pack.zip");
            var overrideBytes = new byte[] { 1, 3, 3, 7 };
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry(
                    "assets/minecraft/textures/block/stone.png",
                    CompressionLevel.NoCompression);
                using (var stream = entry.Open())
                    stream.Write(overrideBytes, 0, overrideBytes.Length);
            }

            using (var source = MinecraftTexturePackSource.OpenArchiveForTests(archivePath))
            {
                Assert.That(
                    source.ReadTextureBytes("minecraft:block/stone"),
                    Is.EqualTo(overrideBytes));

                var fallback = source.ReadTextureBytes("minecraft:block/dirt");
                Assert.That(fallback.Length, Is.GreaterThan(0));
                Assert.That(source.OverrideTextureCount, Is.EqualTo(1));
                Assert.That(source.FallbackTextureCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void ArchiveSource_RejectsNonBlockTextureId()
        {
            var archivePath = Path.Combine(temporaryDirectory, "empty-pack.zip");
            using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
            }

            using (var source = MinecraftTexturePackSource.OpenArchiveForTests(archivePath))
            {
                Assert.Throws<ArgumentException>(() =>
                    source.ReadTextureBytes("minecraft:item/stone"));
            }
        }

        [Test]
        public void Catalog_UsesIgnoredProjectLocalZipAndStableOfficialHash()
        {
            Assert.That(
                MinecraftTexturePackCatalog.RodrigoRelativeZipPath,
                Does.StartWith("LocalTexturePacks/"));
            Assert.That(
                MinecraftTexturePackCatalog.RodrigoZipPath,
                Does.Not.Contain(Path.DirectorySeparatorChar + "Assets" + Path.DirectorySeparatorChar));
            Assert.That(
                MinecraftTexturePackCatalog.RodrigoExpectedSha512,
                Has.Length.EqualTo(128));
            Assert.That(
                MinecraftTexturePackCatalog.ComputeSha512(Array.Empty<byte>()),
                Is.EqualTo(
                    "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce" +
                    "47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"));
        }
    }
}
