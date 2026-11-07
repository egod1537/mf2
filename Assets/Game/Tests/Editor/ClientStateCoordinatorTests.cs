using System;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Storage;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class ClientStateCoordinatorTests
    {
        [Test]
        public void SnapshotDeltaAndClear_AreIsolatedInClientReadModel()
        {
            using (var client = new ClientStateCoordinator())
            {
                var id = new SessionId(Guid.NewGuid());
                var initial = new StoredChunk(int3.zero, new[] { new StoredBlock(0, 3, 0) });
                client.ApplySnapshot(id, 1, 7, new WorldStorageSnapshot(new[] { initial }));

                var position = new int3(1, 0, 0);
                var applied = client.ApplyDelta(id, 1, 8, new[]
                {
                    new WorldBlockDelta(position, Block.Air, new Block(4, 2))
                });

                Assert.That(applied, Is.True);
                Assert.That(client.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(3, 0)));
                Assert.That(client.GetBlockOrAir(position), Is.EqualTo(new Block(4, 2)));
                Assert.That(client.TickIndex, Is.EqualTo(8));

                client.Clear();

                Assert.That(client.HasSession, Is.False);
                Assert.That(client.GetBlockOrAir(position), Is.EqualTo(Block.Air));
            }
        }

        [Test]
        public void DeltaFromAnotherSession_IsRejected()
        {
            using (var client = new ClientStateCoordinator())
            {
                client.ApplySnapshot(
                    new SessionId(Guid.NewGuid()),
                    1,
                    0,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>()));

                var applied = client.ApplyDelta(
                    new SessionId(Guid.NewGuid()),
                    1,
                    1,
                    Array.Empty<WorldBlockDelta>());

                Assert.That(applied, Is.False);
            }
        }

        [Test]
        public void PlayerReplica_ExposesCopiesAndRejectsStaleDelta()
        {
            using (var client = new ClientStateCoordinator())
            {
                var session = new SessionId(Guid.NewGuid());
                var player = new PlayerId(Guid.NewGuid());
                var initial = Model(player, 100, 1);
                Assert.That(
                    client.ApplyPlayerSnapshot(session, 1, 3, new[] { initial }),
                    Is.True);

                Assert.That(client.TryGetPlayer(player, out var firstRead), Is.True);
                var localCopy = Model(player, 1, 99);
                Assert.That(client.TryGetPlayer(player, out var secondRead), Is.True);
                Assert.That(secondRead, Is.EqualTo(firstRead));
                Assert.That(secondRead, Is.Not.EqualTo(localCopy));

                var updated = Model(player, 50, 2);
                Assert.That(
                    client.ApplyPlayerDelta(
                        session,
                        1,
                        4,
                        new[] { PlayerStateChange.Upserted(updated) }),
                    Is.True);
                Assert.That(
                    client.ApplyPlayerDelta(
                        session,
                        1,
                        4,
                        new[] { PlayerStateChange.Upserted(Model(player, 25, 3)) }),
                    Is.False);
                Assert.That(client.TryGetPlayer(player, out var latest), Is.True);
                Assert.That(latest.Health, Is.EqualTo(50));
            }
        }

        [Test]
        public void NewSessionClearsOldPlayerIdentity()
        {
            using (var client = new ClientStateCoordinator())
            {
                var oldSession = new SessionId(Guid.NewGuid());
                var newSession = new SessionId(Guid.NewGuid());
                var player = new PlayerId(Guid.NewGuid());
                client.ApplyPlayerSnapshot(oldSession, 1, 1, new[] { Model(player, 100, 1) });

                client.ApplySnapshot(
                    newSession,
                    2,
                    0,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>()));

                Assert.That(client.SessionId, Is.EqualTo(newSession));
                Assert.That(client.Generation, Is.EqualTo(2));
                Assert.That(client.TryGetPlayer(player, out _), Is.False);
            }
        }

        [Test]
        public void OlderGenerationSnapshotAndDelta_CannotReplaceRestoredReplica()
        {
            using (var client = new ClientStateCoordinator())
            {
                var session = new SessionId(Guid.NewGuid());
                var restoredPosition = new int3(2, 0, 0);
                var stalePosition = new int3(3, 0, 0);
                Assert.That(client.ApplySnapshot(
                    session,
                    4,
                    1,
                    new WorldStorageSnapshot(new[]
                    {
                        new StoredChunk(int3.zero, new[]
                        {
                            new StoredBlock(
                                ChunkCoordinates.ToIndex(restoredPosition),
                                9,
                                0)
                        })
                    })), Is.True);

                Assert.That(client.ApplySnapshot(
                    session,
                    3,
                    99,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>())), Is.False);
                Assert.That(client.ApplyDelta(
                    session,
                    3,
                    100,
                    new[] { new WorldBlockDelta(stalePosition, Block.Air, new Block(10)) }), Is.False);

                Assert.That(client.Generation, Is.EqualTo(4));
                Assert.That(client.GetBlockOrAir(restoredPosition), Is.EqualTo(new Block(9)));
                Assert.That(client.GetBlockOrAir(stalePosition), Is.EqualTo(Block.Air));
            }
        }

        private static PlayerModel Model(PlayerId id, int health, long revision) =>
            new PlayerModel(
                id,
                health,
                PlayerModel.DefaultMaximumHealth,
                health == 0 ? PlayerLifeState.Dead : PlayerLifeState.Alive,
                revision);
    }
}
