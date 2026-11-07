using MF2.Game.Domain;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class PerlinTerrainGeneratorTests
    {
        [Test]
        public void SameConfig_ProducesSameSignatureAndBlocks()
        {
            var config = TerrainConfigTests.CreateConfig(originX: -5, originZ: -3, seed: 777);
            using var firstWorld = new SimulationWorld();
            using var secondWorld = new SimulationWorld();

            var first = new PerlinTerrainGenerator(config, columnsPerBatch: 7).Initialize(firstWorld);
            var second = new PerlinTerrainGenerator(config, columnsPerBatch: 7).Initialize(secondWorld);

            Assert.That(first.Signature, Is.EqualTo(second.Signature));
            Assert.That(first.GeneratedBlockCount, Is.EqualTo(second.GeneratedBlockCount));
            for (var z = config.OriginZ; z <= config.MaximumZ; z++)
            for (var x = config.OriginX; x <= config.MaximumX; x++)
            for (var y = config.MinimumY; y <= config.MaximumY + 1; y++)
            {
                Assert.That(
                    firstWorld.GetBlockOrAir(new int3(x, y, z)),
                    Is.EqualTo(secondWorld.GetBlockOrAir(new int3(x, y, z))));
            }
        }

        [Test]
        public void DifferentSeeds_ChangeAtLeastOneHeight_AndRespectBounds()
        {
            var first = new PerlinTerrainGenerator(TerrainConfigTests.CreateConfig(seed: 101));
            var second = new PerlinTerrainGenerator(TerrainConfigTests.CreateConfig(seed: 202));
            var changed = false;

            for (var z = 0; z < 8; z++)
            for (var x = 0; x < 8; x++)
            {
                var firstHeight = first.SampleHeight(x, z);
                var secondHeight = second.SampleHeight(x, z);
                Assert.That(firstHeight, Is.InRange(0, 8));
                Assert.That(secondHeight, Is.InRange(0, 8));
                changed |= firstHeight != secondHeight;
            }

            Assert.That(changed, Is.True);
        }

        [Test]
        public void PositiveAndNegativeOrigins_AreGeneratedInTheirConfiguredBounds()
        {
            foreach (var origin in new[] { -20, 20 })
            {
                var config = TerrainConfigTests.CreateConfig(originX: origin, originZ: -origin);
                using var world = new SimulationWorld();
                new PerlinTerrainGenerator(config).Initialize(world);

                Assert.That(world.GetBlockOrAir(new int3(origin, config.MinimumY, -origin)).IsAir, Is.False);
                Assert.That(world.GetBlockOrAir(new int3(origin - 1, config.MinimumY, -origin)).IsAir, Is.True);
            }
        }
    }
}
