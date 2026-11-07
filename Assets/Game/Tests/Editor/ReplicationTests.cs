using System;
using System.Runtime.InteropServices;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.World.Core;
using MF2.World.Registry;
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

        [Test]
        public void SnapshotBudget_UsesBoundedPartsAndAppliesOnlyAfterCompleteEnd()
        {
            Assert.That(Marshal.SizeOf<ReplicatedBlock>(),
                Is.EqualTo(WorldSnapshotTransferProtocol.ReplicatedBlockBytes));
            Assert.That(WorldSnapshotTransferProtocol.MaximumEstimatedPartBytes, Is.LessThan(1282));

            var sessionId = Guid.NewGuid();
            var source = new ReplicatedBlock[113];
            for (var i = 0; i < source.Length; i++)
            {
                source[i] = new ReplicatedBlock
                {
                    X = i,
                    Y = i % 17,
                    Z = -i,
                    Id = i == 12 ? BlockTraitsCatalog.WaterStableId : 1u,
                    State = (uint)i
                };
            }

            var partCount = WorldSnapshotTransferProtocol.GetPartCount(source.Length);
            Assert.That(partCount, Is.EqualTo(3));
            var assembler = new WorldSnapshotTransferAssembler();
            var begin = new WorldSnapshotBeginBroadcast
            {
                SessionId = sessionId,
                Generation = 2,
                TickIndex = 9,
                TransferId = 7,
                TotalBlocks = source.Length,
                PartCount = partCount
            };
            Assert.That(assembler.Begin(begin), Is.True);

            for (var partIndex = partCount - 1; partIndex >= 0; partIndex--)
            {
                var blocks = WorldSnapshotTransferProtocol.CopyPart(source, partIndex);
                Assert.That(blocks.Length,
                    Is.LessThanOrEqualTo(WorldSnapshotTransferProtocol.MaximumBlocksPerPart));
                Assert.That(assembler.AddPart(new WorldSnapshotPartBroadcast
                {
                    SessionId = sessionId,
                    Generation = 2,
                    TickIndex = 9,
                    TransferId = 7,
                    PartIndex = partIndex,
                    Blocks = blocks
                }), Is.True);
            }

            Assert.That(assembler.TryComplete(new WorldSnapshotEndBroadcast
            {
                SessionId = sessionId,
                Generation = 2,
                TickIndex = 9,
                TransferId = 7
            }, out var metadata, out var result), Is.True);
            Assert.That(metadata.TotalBlocks, Is.EqualTo(source.Length));
            Assert.That(result, Is.EqualTo(source));
            Assert.That(assembler.IsActive, Is.False);
        }

        [Test]
        public void DeltaBudget_UsesOneBoundedMessageOrAnAtomicSnapshotFallback()
        {
            Assert.That(
                WorldSnapshotTransferProtocol.MaximumDeltaBlocksPerMessage,
                Is.EqualTo(WorldSnapshotTransferProtocol.MaximumBlocksPerPart));
            Assert.That(
                WorldSnapshotTransferProtocol.MaximumEstimatedPartBytes,
                Is.LessThan(1282));
            Assert.That(
                WorldSnapshotTransferProtocol.RequiresSnapshotForDelta(
                    WorldSnapshotTransferProtocol.MaximumDeltaBlocksPerMessage),
                Is.False);
            Assert.That(
                WorldSnapshotTransferProtocol.RequiresSnapshotForDelta(
                    WorldSnapshotTransferProtocol.MaximumDeltaBlocksPerMessage + 1),
                Is.True,
                "A large same-tick delta must use the already bounded, atomically applied snapshot path.");
            Assert.That(
                () => WorldSnapshotTransferProtocol.RequiresSnapshotForDelta(
                    WorldSnapshotTransferProtocol.MaximumSnapshotBlocks + 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void SnapshotTransfer_RejectsPartialDuplicateAndOversizedParts()
        {
            var sessionId = Guid.NewGuid();
            var assembler = new WorldSnapshotTransferAssembler();
            var begin = new WorldSnapshotBeginBroadcast
            {
                SessionId = sessionId,
                Generation = 1,
                TickIndex = 0,
                TransferId = 1,
                TotalBlocks = 49,
                PartCount = 2
            };
            Assert.That(assembler.Begin(begin), Is.True);
            var first = new WorldSnapshotPartBroadcast
            {
                SessionId = sessionId,
                Generation = 1,
                TickIndex = 0,
                TransferId = 1,
                PartIndex = 0,
                Blocks = new ReplicatedBlock[WorldSnapshotTransferProtocol.MaximumBlocksPerPart]
            };
            Assert.That(assembler.AddPart(first), Is.True);
            Assert.That(assembler.AddPart(first), Is.False);
            Assert.That(assembler.TryComplete(new WorldSnapshotEndBroadcast
            {
                SessionId = sessionId,
                Generation = 1,
                TickIndex = 0,
                TransferId = 1
            }, out _, out _), Is.False);

            Assert.That(assembler.AddPart(new WorldSnapshotPartBroadcast
            {
                SessionId = sessionId,
                Generation = 1,
                TickIndex = 0,
                TransferId = 1,
                PartIndex = 1,
                Blocks = new ReplicatedBlock[2]
            }), Is.False);
            Assert.That(assembler.IsActive, Is.False);
        }

        [Test]
        public void WaterSnapshotAndDelta_PreserveLiquidStableIdInClientReplica()
        {
            using var client = new ClientStateCoordinator();
            var session = new SessionId(Guid.NewGuid());
            var water = new Block(BlockTraitsCatalog.WaterStableId, 3);
            var position = new int3(1, 7, -1);
            var local = new int3(1, 7, 15);
            var snapshot = new WorldStorageSnapshot(new[]
            {
                new StoredChunk(new int3(0, 0, -1), new[]
                {
                    new StoredBlock(ChunkCoordinates.ToIndex(local), water.Id, water.State)
                })
            });

            Assert.That(client.ApplySnapshot(session, 1, 0, snapshot), Is.True);
            Assert.That(client.GetBlockOrAir(position), Is.EqualTo(water));
            Assert.That(client.ApplyDelta(session, 1, 1, new[]
            {
                new WorldBlockDelta(position, water, Block.Air)
            }), Is.True);
            Assert.That(client.GetBlockOrAir(position).IsAir, Is.True);
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
