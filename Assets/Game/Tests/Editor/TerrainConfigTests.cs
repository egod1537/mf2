using System;
using MF2.Game.Domain;
using MF2.World.Core;
using NUnit.Framework;

namespace MF2.Tests
{
    public sealed class TerrainConfigTests
    {
        [Test]
        public void ValidFiniteBounds_AreAccepted()
        {
            var config = CreateConfig(originX: -8, originZ: 12);

            Assert.That(config.OriginX, Is.EqualTo(-8));
            Assert.That(config.MaximumZ, Is.EqualTo(19));
        }

        [TestCase(0, 8)]
        [TestCase(-1, 8)]
        [TestCase(8, 0)]
        [TestCase(8, -1)]
        public void NonPositiveHorizontalBounds_AreRejected(int width, int depth)
        {
            Assert.That(
                () => new TerrainConfig(0, 0, width, depth, 0, 8, 4, 2f, 0.1f, 2, 1, new Block(1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(0f, 0.1f, 2)]
        [TestCase(2f, 0f, 2)]
        [TestCase(2f, 0.1f, 0)]
        [TestCase(2f, 0.1f, 9)]
        public void InvalidNoiseSettings_AreRejected(float amplitude, float frequency, int octaves)
        {
            Assert.That(
                () => new TerrainConfig(0, 0, 8, 8, 0, 8, 4, amplitude, frequency, octaves, 1, new Block(1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void UnreachableBaseHeightAndOversizedBudget_AreRejected()
        {
            Assert.That(
                () => new TerrainConfig(0, 0, 8, 8, 0, 8, 9, 2f, 0.1f, 2, 1, new Block(1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new TerrainConfig(0, 0, 64, 64, 0, 64, 8, 2f, 0.1f, 2, 1, new Block(1), 10_000),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void InvalidLayers_AreRejected_AndLegacySolidConfigRemainsAvailable()
        {
            var validPalette = new TerrainSurfacePalette(
                new Block(1),
                new Block(2),
                new Block(3),
                new Block(4));

            Assert.That(
                () => CreateLayeredConfig(palette: validPalette, dirtDepth: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateLayeredConfig(palette: validPalette, dirtDepth: 17),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateLayeredConfig(palette: validPalette, waterLevel: -1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateLayeredConfig(palette: validPalette, waterLevel: 17),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateLayeredConfig(
                    palette: new TerrainSurfacePalette(default, new Block(2), new Block(3), new Block(4))),
                Throws.ArgumentException);
            Assert.That(
                () => CreateLayeredConfig(
                    palette: new TerrainSurfacePalette(new Block(1), new Block(1), new Block(3), new Block(4))),
                Throws.ArgumentException);
            Assert.That(
                () => CreateLayeredConfig(
                    palette: new TerrainSurfacePalette(new Block(1), new Block(2), new Block(3), new Block(3))),
                Throws.ArgumentException);

            var legacy = CreateConfig();
            Assert.That(legacy.WaterEnabled, Is.False);
            Assert.That(legacy.SolidBlock, Is.EqualTo(new Block(1)));
            Assert.That(legacy.Profile, Is.EqualTo(TerrainGenerationProfile.Normal));
            Assert.That(
                () => new TerrainConfig(
                    0, 0, 1, 1, 0, 8, 4, 2f, 0.1f, 2, 1, new Block(1),
                    profile: (TerrainGenerationProfile)99),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        internal static TerrainConfig CreateConfig(
            int originX = 0,
            int originZ = 0,
            int seed = 123,
            int width = 8,
            int depth = 8,
            TerrainGenerationProfile profile = TerrainGenerationProfile.Normal) =>
            new TerrainConfig(
                originX,
                originZ,
                width,
                depth,
                minimumY: 0,
                maximumY: 8,
                baseHeight: 4,
                amplitude: 2f,
                frequency: 0.08f,
                octaves: 3,
                seed,
                new Block(1),
                profile: profile);

        internal static TerrainConfig CreateLayeredConfig(
            int originX = 0,
            int originZ = 0,
            int width = 1,
            int depth = 1,
            int minimumY = 0,
            int maximumY = 16,
            int baseHeight = 12,
            int seed = 123,
            TerrainSurfacePalette? palette = null,
            int dirtDepth = 3,
            int waterLevel = 7) =>
            new TerrainConfig(
                originX,
                originZ,
                width,
                depth,
                minimumY,
                maximumY,
                baseHeight,
                amplitude: 0.5f,
                frequency: 0.08f,
                octaves: 1,
                seed,
                palette ?? new TerrainSurfacePalette(
                    new Block(1),
                    new Block(2),
                    new Block(3),
                    new Block(4)),
                dirtDepth,
                waterLevel);
    }
}
