using MF2.Game.Domain;
using MF2.World.Core;
using MF2.World.Registry;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class SpawnLocatorTests
    {
        [Test]
        public void PlayersReceiveDistinctRepeatablePositionsAboveSolidColumns()
        {
            var config = TerrainConfigTests.CreateConfig(originX: -4, originZ: -4);
            using var world = new SimulationWorld();
            new PerlinTerrainGenerator(config).Initialize(world);
            var locator = new SpawnLocator();

            Assert.That(locator.TryLocate(world, config, 0, out var first), Is.True);
            Assert.That(locator.TryLocate(world, config, 1, out var second), Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(locator.TryLocate(world, config, 0, out var repeated), Is.True);
            Assert.That(repeated, Is.EqualTo(first));

            AssertValid(first);
            AssertValid(second);

            void AssertValid(float3 candidate)
            {
                var x = (int)math.floor(candidate.x);
                var z = (int)math.floor(candidate.z);
                var surface = (int)math.floor(candidate.y - 0.05f) - 1;
                Assert.That(config.ContainsColumn(x, z), Is.True);
                Assert.That(world.GetBlockOrAir(new int3(x, surface, z)).IsAir, Is.False);
                Assert.That(world.GetBlockOrAir(new int3(x, surface + 1, z)).IsAir, Is.True);
                Assert.That(world.GetBlockOrAir(new int3(x, surface + 2, z)).IsAir, Is.True);
            }
        }

        [Test]
        public void DrySpawn_NeverTreatsWaterAsFloorOrDryHeadroom()
        {
            var config = TerrainConfigTests.CreateLayeredConfig(
                width: 4,
                depth: 4,
                baseHeight: 4);
            using var world = new SimulationWorld();
            var locator = new SpawnLocator();
            var water = new Block(BlockTraitsCatalog.WaterStableId);

            for (var z = config.OriginZ; z <= config.MaximumZ; z++)
            for (var x = config.OriginX; x <= config.MaximumX; x++)
                world.Enqueue(WorldCommand.SetBlock(new int3(x, 7, z), water));
            world.FlushAndCapture();

            Assert.That(locator.TryLocate(world, config, 0, out _), Is.False);

            world.Enqueue(WorldCommand.RemoveBlock(new int3(2, 7, 1)));
            world.Enqueue(WorldCommand.SetBlock(new int3(2, 4, 1), config.StoneBlock));
            world.FlushAndCapture();

            Assert.That(locator.TryLocate(world, config, 0, out var position), Is.True);
            Assert.That(position.x, Is.EqualTo(2.5f));
            Assert.That(position.z, Is.EqualTo(1.5f));
            Assert.That(position.y, Is.EqualTo(5.05f).Within(0.0001f));
        }

        [Test]
        public void RestoredWorldSnapshot_FindsDrySolidFloorWithoutTerrainConfig()
        {
            using var world = new SimulationWorld();
            var locator = new SpawnLocator();
            var water = new Block(BlockTraitsCatalog.WaterStableId);
            var stone = new Block(StableBlockId.Compute("minecraft:stone"));

            world.Enqueue(WorldCommand.SetBlock(new int3(0, 7, 0), water));
            world.Enqueue(WorldCommand.SetBlock(new int3(1, 4, 0), stone));
            world.Enqueue(WorldCommand.SetBlock(new int3(1, 5, 0), water));
            world.Enqueue(WorldCommand.SetBlock(new int3(2, 3, 0), stone));
            world.FlushAndCapture();

            var snapshot = world.CaptureSnapshot();
            Assert.That(locator.TryLocate(world, snapshot, 0, out var position), Is.True);
            Assert.That(position.x, Is.EqualTo(2.5f));
            Assert.That(position.y, Is.EqualTo(4.05f).Within(0.0001f));
            Assert.That(position.z, Is.EqualTo(0.5f));
        }
    }
}
