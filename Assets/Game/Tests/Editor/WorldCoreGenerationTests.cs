using MF2.Game.Domain;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class WorldCoreGenerationTests
    {
        [Test]
        public void Generation_UsesBatchedCommandsAndProducesSolidSurfaceWithAirAbove()
        {
            var config = TerrainConfigTests.CreateConfig(width: 32, depth: 2);
            var generator = new PerlinTerrainGenerator(config, columnsPerBatch: 5);
            using var world = new SimulationWorld();

            var result = generator.Initialize(world);

            Assert.That(result.FlushCount, Is.GreaterThan(1));
            foreach (var x in new[] { 15, 16 })
            {
                var height = generator.SampleHeight(x, 0);
                Assert.That(world.GetBlockOrAir(new int3(x, height, 0)).IsAir, Is.False);
                Assert.That(world.GetBlockOrAir(new int3(x, height + 1, 0)).IsAir, Is.True);
            }
        }

        [Test]
        public void GameSession_InitializesOnceBeforeStart()
        {
            using var world = new SimulationWorld();
            using var session = new GameSession(
                new PlayerRegistry(), world, new CommandProcessor(), new SessionClock());
            var initializer = new PerlinTerrainGenerator(TerrainConfigTests.CreateConfig());

            var result = session.InitializeWorld(initializer);

            Assert.That(session.Seed, Is.EqualTo(result.Config.Seed));
            Assert.That(() => session.InitializeWorld(initializer), Throws.InvalidOperationException);
            session.Start();
            Assert.That(() => session.InitializeWorld(initializer), Throws.InvalidOperationException);
        }
    }
}
