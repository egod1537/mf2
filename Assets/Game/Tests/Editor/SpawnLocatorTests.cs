using MF2.Game.Domain;
using MF2.World.Core;
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
    }
}
