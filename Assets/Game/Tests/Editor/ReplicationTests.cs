using System;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Storage;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class ReplicationTests
    {
        [Test]
        public void TickDelta_IsManagedCopyAndSurvivesTheNextWorldFlush()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateRunningSession(world, out var player))
            {
                var firstPosition = new int3(1, 2, 3);
                var firstBlock = new Block(5, 1);
                session.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(firstPosition, firstBlock)));
                var first = session.Step(new SimulationStep(0f));

                session.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(new int3(9, 9, 9), new Block(6))));
                session.Step(new SimulationStep(0f));

                Assert.That(first.Delta.BlockChanges.Count, Is.EqualTo(1));
                Assert.That(first.Delta.BlockChanges[0].Position, Is.EqualTo(firstPosition));
                Assert.That(first.Delta.BlockChanges[0].Final, Is.EqualTo(firstBlock));
            }
        }

        [Test]
        public void ClientReplicaMutation_DoesNotMutateAuthoritativeWorld()
        {
            using (var serverWorld = new SimulationWorld())
            using (var server = CreateRunningSession(serverWorld, out _))
            using (var client = new ClientStateCoordinator())
            {
                var position = new int3(7, 0, -2);
                client.ApplySnapshot(
                    server.Id,
                    server.Generation,
                    server.TickIndex,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>()));
                Assert.That(client.ApplyDelta(
                    server.Id,
                    server.Generation,
                    1,
                    new[] { new WorldBlockDelta(position, Block.Air, new Block(42)) }), Is.True);

                Assert.That(client.GetBlockOrAir(position), Is.EqualTo(new Block(42)));
                Assert.That(server.World.GetBlockOrAir(position), Is.EqualTo(Block.Air));
            }
        }

        [Test]
        public void LateJoin_RejectsWorldAndPlayerDeltasUntilMatchingSnapshotsArrive()
        {
            using (var client = new ClientStateCoordinator())
            {
                var session = new SessionId(Guid.NewGuid());
                var player = new PlayerId(Guid.NewGuid());
                var position = new int3(2, 0, 0);
                var playerChange = PlayerStateChange.Upserted(new PlayerModel(player));

                Assert.That(client.ApplyDelta(
                    session,
                    1,
                    2,
                    new[] { new WorldBlockDelta(position, Block.Air, new Block(8)) }), Is.False);
                Assert.That(client.ApplyPlayerDelta(session, 1, 2, new[] { playerChange }), Is.False);

                client.ApplySnapshot(
                    session,
                    1,
                    1,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>()));
                Assert.That(client.ApplyPlayerDelta(session, 1, 2, new[] { playerChange }), Is.False,
                    "The world snapshot cannot substitute for the player snapshot barrier.");

                Assert.That(client.ApplyPlayerSnapshot(
                    session,
                    1,
                    1,
                    Array.Empty<PlayerModel>()), Is.True);
                Assert.That(client.ApplyDelta(
                    session,
                    1,
                    2,
                    new[] { new WorldBlockDelta(position, Block.Air, new Block(8)) }), Is.True);
                Assert.That(client.ApplyPlayerDelta(session, 1, 2, new[] { playerChange }), Is.True);
                Assert.That(client.GetBlockOrAir(position), Is.EqualTo(new Block(8)));
                Assert.That(client.TryGetPlayer(player, out _), Is.True);
            }
        }

        private static GameSession CreateRunningSession(
            SimulationWorld world,
            out PlayerId player)
        {
            var session = new GameSession(
                new PlayerRegistry(),
                world,
                new CommandProcessor(),
                new SessionClock());
            player = new PlayerId(Guid.NewGuid());
            session.Start();
            session.TryRegisterPlayer(player);
            return session;
        }
    }
}
