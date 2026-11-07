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

        [Test]
        public void LayeredColumn_UsesStoneDirtAndGrassAboveWaterLine()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(baseHeight: 12);
            using var world = new SimulationWorld();
            var generator = new PerlinTerrainGenerator(config);

            Assert.That(generator.SampleHeight(0, 0), Is.EqualTo(12));
            var result = generator.Initialize(world);

            AssertRange(world, 0, 0, 0, 8, config.StoneBlock);
            AssertRange(world, 0, 0, 9, 11, config.DirtBlock);
            Assert.That(world.GetBlockOrAir(new int3(0, 12, 0)), Is.EqualTo(config.GrassBlock));
            Assert.That(world.GetBlockOrAir(new int3(0, 13, 0)).IsAir, Is.True);
            Assert.That(result.GeneratedBlockCount, Is.EqualTo(13));
        }

        [Test]
        public void Shoreline_PlacesGrassAtWaterLevelWithoutWater()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(baseHeight: 7);
            using var world = new SimulationWorld();
            var generator = new PerlinTerrainGenerator(config);

            Assert.That(generator.SampleHeight(0, 0), Is.EqualTo(7));
            generator.Initialize(world);

            Assert.That(world.GetBlockOrAir(new int3(0, 7, 0)), Is.EqualTo(config.GrassBlock));
            Assert.That(world.GetBlockOrAir(new int3(0, 8, 0)).IsAir, Is.True);
        }

        [Test]
        public void UnderwaterColumn_UsesDirtBedAndFillsWaterToConfiguredLevel()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(baseHeight: 4);
            using var world = new SimulationWorld();
            var generator = new PerlinTerrainGenerator(config);

            Assert.That(generator.SampleHeight(0, 0), Is.EqualTo(4));
            var result = generator.Initialize(world);

            Assert.That(world.GetBlockOrAir(new int3(0, 0, 0)), Is.EqualTo(config.StoneBlock));
            AssertRange(world, 0, 0, 1, 4, config.DirtBlock);
            AssertRange(world, 0, 0, 5, 7, config.WaterBlock);
            Assert.That(world.GetBlockOrAir(new int3(0, 8, 0)).IsAir, Is.True);
            Assert.That(result.GeneratedBlockCount, Is.EqualTo(8));
        }

        [Test]
        public void ShallowColumn_ClipsDirtAtMinimumY()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(baseHeight: 0);
            using var world = new SimulationWorld();
            var generator = new PerlinTerrainGenerator(config);

            Assert.That(generator.SampleHeight(0, 0), Is.EqualTo(0));
            generator.Initialize(world);

            Assert.That(world.GetBlockOrAir(new int3(0, 0, 0)), Is.EqualTo(config.DirtBlock));
            AssertRange(world, 0, 0, 1, 7, config.WaterBlock);
            Assert.That(world.GetBlockOrAir(new int3(0, -1, 0)).IsAir, Is.True);
        }

        [Test]
        public void BatchInvariant_ProducesIdenticalSignatureCountsAndBlocks()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(
                originX: -4,
                originZ: -3,
                width: 8,
                depth: 6,
                baseHeight: 7);
            using var firstWorld = new SimulationWorld();
            using var secondWorld = new SimulationWorld();
            using var thirdWorld = new SimulationWorld();

            var first = new PerlinTerrainGenerator(config, 1).Initialize(firstWorld);
            var second = new PerlinTerrainGenerator(config, 7).Initialize(secondWorld);
            var third = new PerlinTerrainGenerator(config, 256).Initialize(thirdWorld);

            Assert.That(second.Signature, Is.EqualTo(first.Signature));
            Assert.That(third.Signature, Is.EqualTo(first.Signature));
            Assert.That(second.GeneratedBlockCount, Is.EqualTo(first.GeneratedBlockCount));
            Assert.That(third.GeneratedBlockCount, Is.EqualTo(first.GeneratedBlockCount));
            for (var z = config.OriginZ; z <= config.MaximumZ; z++)
            for (var x = config.OriginX; x <= config.MaximumX; x++)
            for (var y = config.MinimumY; y <= config.MaximumY; y++)
            {
                var expected = firstWorld.GetBlockOrAir(new int3(x, y, z));
                Assert.That(secondWorld.GetBlockOrAir(new int3(x, y, z)), Is.EqualTo(expected));
                Assert.That(thirdWorld.GetBlockOrAir(new int3(x, y, z)), Is.EqualTo(expected));
            }
        }

        [Test]
        public void Signature_DistinguishesLayerRecipeWithIdenticalHeights()
        {
            var firstConfig = TerrainConfigTests.CreateLayeredConfig(dirtDepth: 3);
            var secondConfig = TerrainConfigTests.CreateLayeredConfig(dirtDepth: 2);
            using var firstWorld = new SimulationWorld();
            using var secondWorld = new SimulationWorld();

            var first = new PerlinTerrainGenerator(firstConfig).Initialize(firstWorld);
            var second = new PerlinTerrainGenerator(secondConfig).Initialize(secondWorld);

            Assert.That(first.Signature, Is.Not.EqualTo(second.Signature));
        }

        private static void AssertRange(
            SimulationWorld world,
            int x,
            int z,
            int minimumY,
            int maximumY,
            Block expected)
        {
            for (var y = minimumY; y <= maximumY; y++)
                Assert.That(world.GetBlockOrAir(new int3(x, y, z)), Is.EqualTo(expected));
        }
    }
}
