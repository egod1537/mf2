using System;
using System.IO;
using MF2.Game.Domain;
using MF2.Game.Networking;
using MF2.Game.Persistence;
using NUnit.Framework;

namespace MF2.Tests
{
    public sealed class PlayerActionTests
    {
        [Test]
        public void Dash_ConsumesChargesAtTickBoundaryAndRechargesDeterministically()
        {
            var registry = new PlayerRegistry();
            using var world = new SimulationWorld();
            using var session = new GameSession(
                registry, world, new CommandProcessor(), new SessionClock());
            session.Start();
            var player = new PlayerId(Guid.NewGuid());
            session.TryRegisterPlayer(player);

            Assert.That(session.SubmitCommand(player, GameCommand.ApplyPlayerAction(
                PlayerActionCommand.Dash())).IsAccepted, Is.True);
            Assert.That(registry.TryGet(player, out var before), Is.True);
            Assert.That(before.Actions.DashCharges, Is.EqualTo(3));

            session.Step(new SimulationStep(1f / 30f));
            Assert.That(registry.TryGet(player, out var after), Is.True);
            Assert.That(after.Actions.DashCharges, Is.EqualTo(2));
            Assert.That(after.Actions.DashSequence, Is.EqualTo(1));

            for (var i = 0; i < PlayerActionState.DashRechargeTicks; i++)
                session.Step(new SimulationStep(1f / 30f));
            Assert.That(registry.TryGet(player, out var recharged), Is.True);
            Assert.That(recharged.Actions.DashCharges, Is.EqualTo(3));
        }

        [Test]
        public void Attack_AppliesServerOwnedDamageAndCooldown()
        {
            var registry = new PlayerRegistry();
            using var world = new SimulationWorld();
            using var session = new GameSession(
                registry, world, new CommandProcessor(), new SessionClock());
            session.Start();
            var attacker = new PlayerId(Guid.NewGuid());
            var target = new PlayerId(Guid.NewGuid());
            session.TryRegisterPlayer(attacker);
            session.TryRegisterPlayer(target);

            var command = GameCommand.ApplyPlayerAction(PlayerActionCommand.Attack(target));
            session.SubmitCommand(attacker, command);
            session.SubmitCommand(attacker, command);
            var result = session.Step(new SimulationStep(1f / 30f));

            Assert.That(result.CommandsProcessed, Is.EqualTo(1));
            Assert.That(registry.TryGet(target, out var victim), Is.True);
            Assert.That(victim.Health, Is.EqualTo(75));
            Assert.That(registry.TryGet(attacker, out var actor), Is.True);
            Assert.That(actor.Actions.AttackSequence, Is.EqualTo(1));
        }

        [Test]
        public void ActionEnvelope_HasNoClientSuppliedTargetIdentity()
        {
            var message = PlayerActionBroadcast.Attack(4, 0.5f, 0.5f);
            Assert.That(message.TryGetDirection(out var direction), Is.True);
            Assert.That(direction.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(typeof(PlayerActionBroadcast).GetField("Target"), Is.Null);
            Assert.That(typeof(PlayerActionBroadcast).GetField("PlayerId"), Is.Null);
        }

        [Test]
        public void SessionJsonStorage_RoundTripsActionState()
        {
            var path = Path.Combine(Path.GetTempPath(), "mf2-action-" + Guid.NewGuid().ToString("N"));
            try
            {
                var registry = new PlayerRegistry();
                using var world = new SimulationWorld();
                using var session = new GameSession(
                    registry, world, new CommandProcessor(), new SessionClock());
                session.Start();
                var player = new PlayerId(Guid.NewGuid());
                session.TryRegisterPlayer(player);
                session.SubmitCommand(player, GameCommand.ApplyPlayerAction(
                    PlayerActionCommand.Dash()));
                session.Step(new SimulationStep(1f / 30f));

                var storage = new SessionJsonStorage(path);
                storage.Save(session.CaptureArchive());
                var restored = storage.LoadCurrent().Players[0];
                Assert.That(restored.Actions.DashCharges, Is.EqualTo(2));
                Assert.That(restored.Actions.DashSequence, Is.EqualTo(1));
            }
            finally
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }
    }
}
