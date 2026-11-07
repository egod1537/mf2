using System;
using MF2.Game.Domain;
using MF2.Game.Networking;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class GameSessionTests
    {
        [Test]
        public void Step_AppliesQueuedCommandAndCapturesManagedDelta()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var player = new PlayerId(Guid.Parse("00000000-0000-0000-0000-000000000002"));
                session.Start();
                Assert.That(session.TryRegisterPlayer(player), Is.True);
                Assert.That(session.TryRegisterPlayer(player), Is.False);

                var position = new int3(4, 5, 6);
                var block = new Block(11, 3);
                Assert.That(
                    session.SubmitCommand(player, GameCommand.ApplyWorld(
                        WorldCommand.SetBlock(position, block))).IsAccepted,
                    Is.True);

                var firstTick = session.Step(new SimulationStep(1f / 30f));
                session.Step(new SimulationStep(1f / 30f));

                Assert.That(firstTick.TickIndex, Is.EqualTo(1));
                Assert.That(firstTick.CommandsProcessed, Is.EqualTo(1));
                Assert.That(firstTick.Delta.BlockChanges.Count, Is.EqualTo(1));
                Assert.That(firstTick.PlayerChanges.Count, Is.EqualTo(1));
                Assert.That(firstTick.PlayerChanges[0].PlayerId, Is.EqualTo(player));
                Assert.That(firstTick.Delta.BlockChanges[0].Position, Is.EqualTo(position));
                Assert.That(firstTick.Delta.BlockChanges[0].Final, Is.EqualTo(block));
                Assert.That(session.World.GetBlockOrAir(position), Is.EqualTo(block));
                Assert.That(session.TickIndex, Is.EqualTo(2));
            }
        }

        [Test]
        public void Snapshot_UsesGuidIdentityAndIncludesManagedWorld()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var player = new PlayerId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
                session.Start();
                session.TryRegisterPlayer(player);
                session.Step(new SimulationStep(0f));

                var snapshot = session.CaptureSnapshot();

                Assert.That(snapshot.Id.Value, Is.Not.EqualTo(Guid.Empty));
                Assert.That(snapshot.Generation, Is.EqualTo(1));
                Assert.That(snapshot.PlayerIds, Is.EquivalentTo(new[] { player }));
                Assert.That(snapshot.Players.Count, Is.EqualTo(1));
                Assert.That(snapshot.Players[0].Id, Is.EqualTo(player));
                Assert.That(snapshot.TickIndex, Is.EqualTo(1));
                Assert.That(snapshot.World, Is.Not.Null);
            }
        }

        [Test]
        public void PlayerChange_IsCapturedOnlyByCompletedSessionTick()
        {
            using (var world = new SimulationWorld())
            {
                var registry = new PlayerRegistry();
                using (var session = new GameSession(
                           registry,
                           world,
                           new CommandProcessor(),
                           new SessionClock()))
                {
                    var player = new PlayerId(Guid.NewGuid());
                    var health = new HealthSystem(registry);
                    session.Start();
                    session.TryRegisterPlayer(player);
                    session.Step(new SimulationStep(0f));

                    Assert.That(health.ApplyDamage(player, 25), Is.True);
                    var tick = session.Step(new SimulationStep(0f));

                    Assert.That(tick.PlayerChanges.Count, Is.EqualTo(1));
                    Assert.That(tick.PlayerChanges[0].Snapshot.Health, Is.EqualTo(75));
                    Assert.That(tick.PlayerChanges[0].PlayerId, Is.EqualTo(player));
                }
            }
        }

        [Test]
        public void StoppedSession_RejectsFurtherCommands()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var player = new PlayerId(Guid.NewGuid());
                session.Start();
                session.TryRegisterPlayer(player);
                session.Stop();

                var result = session.SubmitCommand(
                    player,
                    GameCommand.ApplyWorld(WorldCommand.RemoveBlock(int3.zero)));

                Assert.That(session.Phase, Is.EqualTo(SessionPhase.Stopping));
                Assert.That(result.Status, Is.EqualTo(CommandStatus.SessionNotRunning));
            }
        }

        [Test]
        public void WorldCommandBroadcast_RoundTripsManagedDto()
        {
            var source = WorldCommand.FillRegion(
                new int3(-2, 3, 7),
                new int3(4, 5, 9),
                new Block(42, 11));

            var message = WorldCommandBroadcast.FromCommand(source, 17);

            Assert.That(message.Sequence, Is.EqualTo(17));
            Assert.That(message.TryToCommand(out var restored), Is.True);
            Assert.That(restored.Type, Is.EqualTo(WorldCommandType.FillRegion));
            Assert.That(restored.Min, Is.EqualTo(source.Min));
            Assert.That(restored.Max, Is.EqualTo(source.Max));
            Assert.That(restored.Block, Is.EqualTo(source.Block));
        }

        [Test]
        public void WorldCommandBroadcast_RejectsInvalidAirState()
        {
            var message = new WorldCommandBroadcast
            {
                Sequence = 1,
                Type = (byte)WorldCommandType.SetBlock,
                BlockState = 1
            };

            Assert.That(message.TryToCommand(out _), Is.False);
        }

        [Test]
        public void WorldCommandBroadcast_RejectsNonCanonicalSingleCellPayloads()
        {
            var removalWithBlockPayload = new WorldCommandBroadcast
            {
                Sequence = 1,
                Type = (byte)WorldCommandType.RemoveBlock,
                BlockId = 12
            };
            var setWithDifferentMaximum = new WorldCommandBroadcast
            {
                Sequence = 2,
                Type = (byte)WorldCommandType.SetBlock,
                MaxX = 1,
                BlockId = 12
            };

            Assert.That(removalWithBlockPayload.TryToCommand(out _), Is.False);
            Assert.That(setWithDifferentMaximum.TryToCommand(out _), Is.False);
        }

        [Test]
        public void Pause_BlocksTicksUntilExplicitResume()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                session.Start();
                session.Pause();

                Assert.That(session.Phase, Is.EqualTo(SessionPhase.Paused));
                Assert.Throws<InvalidOperationException>(() =>
                    session.Step(new SimulationStep(1f / 30f)));

                session.Resume();
                session.Step(new SimulationStep(1f / 30f));

                Assert.That(session.TickIndex, Is.EqualTo(1));
            }
        }

        [Test]
        public void CaptureArchive_RequiresACompletedTickBoundary()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var player = new PlayerId(Guid.NewGuid());
                session.Start();
                session.TryRegisterPlayer(player);

                Assert.That(session.IsArchiveBoundaryStable, Is.False);
                Assert.Throws<InvalidOperationException>(() => session.CaptureArchive());

                session.Step(new SimulationStep(0f));

                Assert.That(session.IsArchiveBoundaryStable, Is.True);
                Assert.DoesNotThrow(() => session.CaptureArchive());

                session.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(int3.zero, new Block(1))));
                Assert.Throws<InvalidOperationException>(() => session.CaptureArchive());
            }
        }

        [Test]
        public void SubmitCommand_RejectsUnknownPlayerAndOversizedFillBeforeTick()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var registered = new PlayerId(Guid.NewGuid());
                var unknown = new PlayerId(Guid.NewGuid());
                session.Start();
                session.TryRegisterPlayer(registered);

                var valid = GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(int3.zero, new Block(1)));
                Assert.That(
                    session.SubmitCommand(unknown, valid).Status,
                    Is.EqualTo(CommandStatus.UnknownPlayer));

                var oversized = GameCommand.ApplyWorld(WorldCommand.FillRegion(
                    int3.zero,
                    new int3(WorldCore.DefaultMaxFillBlocksPerCommand, 0, 0),
                    new Block(1)));
                Assert.That(
                    session.SubmitCommand(registered, oversized).Status,
                    Is.EqualTo(CommandStatus.InvalidCommand));

                var tick = session.Step(new SimulationStep(0f));
                Assert.That(tick.CommandsProcessed, Is.Zero);
            }
        }

        [Test]
        public void Dispose_IsIdempotentAndRejectsFurtherAccess()
        {
            var world = new SimulationWorld();
            var session = CreateSession(world);
            session.Start();

            session.Dispose();
            Assert.DoesNotThrow(session.Dispose);
            Assert.Throws<ObjectDisposedException>(() => session.CaptureSnapshot());
            Assert.That(world.LoadedChunkCount, Is.Zero,
                "GameSession does not own the separately registered SimulationWorld disposal.");

            world.Dispose();
        }

        [Test]
        public void StopAndDispose_AreIdempotentAtTheirContractBoundaries()
        {
            var world = new SimulationWorld();
            var session = CreateSession(world);
            session.Start();

            Assert.DoesNotThrow(session.Stop);
            Assert.DoesNotThrow(session.Stop);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Stopping));
            Assert.Throws<InvalidOperationException>(() =>
                session.Step(new SimulationStep(0f)));

            Assert.DoesNotThrow(session.Dispose);
            Assert.DoesNotThrow(session.Dispose);
            Assert.That(session.Phase, Is.EqualTo(SessionPhase.Disposed));
            world.Dispose();
        }

        [Test]
        public void SeparateSessions_DoNotSharePlayersTicksOrWorldState()
        {
            using (var firstWorld = new SimulationWorld())
            using (var secondWorld = new SimulationWorld())
            using (var first = CreateSession(firstWorld))
            using (var second = CreateSession(secondWorld))
            {
                var player = new PlayerId(Guid.NewGuid());
                var position = new int3(9, 8, 7);
                first.Start();
                second.Start();
                first.TryRegisterPlayer(player);
                first.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(position, new Block(13))));
                first.Step(new SimulationStep(0f));

                Assert.That(first.Id, Is.Not.EqualTo(second.Id));
                Assert.That(first.Seed, Is.Not.EqualTo(second.Seed));
                Assert.That(first.TickIndex, Is.EqualTo(1));
                Assert.That(second.TickIndex, Is.Zero);
                Assert.That(second.Players.Count, Is.Zero);
                Assert.That(second.World.GetBlockOrAir(position), Is.EqualTo(Block.Air));
            }
        }

        [Test]
        public void CommandAuthorizer_CanRejectRegisteredPlayerBeforeQueueing()
        {
            using (var world = new SimulationWorld())
            using (var session = new GameSession(
                       new PlayerRegistry(),
                       world,
                       new CommandProcessor(),
                       new SessionClock(),
                       new DenyCommandAuthorizer()))
            {
                var player = new PlayerId(Guid.NewGuid());
                session.Start();
                session.TryRegisterPlayer(player);

                var result = session.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(int3.zero, new Block(1))));
                var tick = session.Step(new SimulationStep(0f));

                Assert.That(result.Status, Is.EqualTo(CommandStatus.Unauthorized));
                Assert.That(tick.CommandsProcessed, Is.Zero);
            }
        }

        [Test]
        public void TickObserverFailure_DoesNotInterruptOtherObserversOrRepeatFlush()
        {
            using (var world = new SimulationWorld())
            using (var session = CreateSession(world))
            {
                var player = new PlayerId(Guid.NewGuid());
                var position = new int3(3, 2, 1);
                var successfulObserverCalls = 0;
                var reportedFailures = 0;
                session.TickCompleted += _ => throw new InvalidOperationException("observer failure");
                session.TickCompleted += _ => successfulObserverCalls++;
                session.TickObserverFailed += _ => reportedFailures++;
                session.Start();
                session.TryRegisterPlayer(player);
                session.SubmitCommand(player, GameCommand.ApplyWorld(
                    WorldCommand.SetBlock(position, new Block(7))));

                SessionTickResult first = null;
                Assert.DoesNotThrow(() => first = session.Step(new SimulationStep(0f)));
                var second = session.Step(new SimulationStep(0f));

                Assert.That(first.TickIndex, Is.EqualTo(1));
                Assert.That(first.Delta.BlockChanges.Count, Is.EqualTo(1));
                Assert.That(second.TickIndex, Is.EqualTo(2));
                Assert.That(second.Delta.BlockChanges.Count, Is.Zero);
                Assert.That(successfulObserverCalls, Is.EqualTo(2));
                Assert.That(reportedFailures, Is.EqualTo(2));
            }
        }

        private sealed class DenyCommandAuthorizer : IGameCommandAuthorizer
        {
            public bool IsAuthorized(PlayerId sender, in GameCommand command) => false;
        }

        private static GameSession CreateSession(SimulationWorld world) =>
            new GameSession(
                new PlayerRegistry(),
                world,
                new CommandProcessor(),
                new SessionClock());
    }
}
