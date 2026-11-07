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

        internal static TerrainConfig CreateConfig(
            int originX = 0,
            int originZ = 0,
            int seed = 123,
            int width = 8,
            int depth = 8) =>
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
                new Block(1));
    }
}
