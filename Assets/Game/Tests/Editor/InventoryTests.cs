using System;
using System.IO;
using MF2.Game.Domain;
using MF2.Game.Persistence;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using NUnit.Framework;

namespace MF2.Tests
{
    public sealed class InventoryTests
    {
        [Test]
        public void DefaultInventory_MatchesCaveworksCapacitiesAndHasBoundedStarterQuickbar()
        {
            var inventory = new InventoryState();
            Assert.That(inventory.CaptureSlots(InventoryContainerKind.Bag).Length, Is.EqualTo(30));
            Assert.That(inventory.CaptureSlots(InventoryContainerKind.Quickbar).Length, Is.EqualTo(7));
            Assert.That(inventory.CaptureSlots(InventoryContainerKind.Equipment).Length, Is.EqualTo(6));
            Assert.That(inventory.GetSlot(InventoryContainerKind.Quickbar, 0),
                Is.EqualTo(new ItemStack(ItemCatalog.Stone, 64)));
            Assert.That(inventory.SelectedQuickbarSlot, Is.Zero);
        }

        [Test]
        public void MoveMergeSwapAndSplit_PreserveTotalItems()
        {
            var quickbar = new ItemStack[InventoryState.QuickbarSlotCount];
            quickbar[0] = new ItemStack(ItemCatalog.Stone, 10);
            quickbar[1] = new ItemStack(ItemCatalog.Stone, 20);
            quickbar[2] = new ItemStack(ItemCatalog.Dirt, 4);
            var inventory = new InventoryState(
                new ItemStack[InventoryState.BagSlotCount],
                quickbar,
                new ItemStack[InventoryState.EquipmentSlotCount],
                0);

            Assert.That(inventory.TryApply(
                InventoryCommand.Move(InventoryContainerKind.Quickbar, 0,
                    InventoryContainerKind.Quickbar, 1, 5), out var merged), Is.True);
            Assert.That(merged.GetSlot(InventoryContainerKind.Quickbar, 0).Count, Is.EqualTo(5));
            Assert.That(merged.GetSlot(InventoryContainerKind.Quickbar, 1).Count, Is.EqualTo(25));

            Assert.That(merged.TryApply(
                InventoryCommand.Move(InventoryContainerKind.Quickbar, 2,
                    InventoryContainerKind.Bag, 0, 4), out var moved), Is.True);
            Assert.That(moved.GetSlot(InventoryContainerKind.Quickbar, 2).IsEmpty, Is.True);
            Assert.That(moved.GetSlot(InventoryContainerKind.Bag, 0).Count, Is.EqualTo(4));
            Assert.That(Count(moved, ItemCatalog.Stone), Is.EqualTo(30));
            Assert.That(Count(moved, ItemCatalog.Dirt), Is.EqualTo(4));
        }

        [Test]
        public void InvalidInventoryCommands_AreRejectedWithoutMutation()
        {
            var inventory = new InventoryState();
            Assert.That(inventory.TryApply(
                InventoryCommand.Move(InventoryContainerKind.Quickbar, 0,
                    InventoryContainerKind.Bag, 30, 1), out var result), Is.False);
            Assert.That(result, Is.SameAs(inventory));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemStack(0xdeadbeef, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemStack(ItemCatalog.Stone, 65));
        }

        [Test]
        public void GameSession_AppliesInventoryOnlyAtAuthoritativeTick()
        {
            var registry = new PlayerRegistry();
            using var world = new SimulationWorld();
            using var session = new GameSession(
                registry, world, new CommandProcessor(), new SessionClock());
            session.Start();
            var player = new PlayerId(Guid.NewGuid());
            Assert.That(session.TryRegisterPlayer(player), Is.True);

            var accepted = session.SubmitCommand(player, GameCommand.ApplyInventory(
                InventoryCommand.SelectQuickbar(3)));
            Assert.That(accepted.IsAccepted, Is.True);
            Assert.That(registry.TryGet(player, out var before), Is.True);
            Assert.That(before.Inventory.SelectedQuickbarSlot, Is.Zero);

            var tick = session.Step(new SimulationStep(1f / 30f));
            Assert.That(tick.CommandsProcessed, Is.EqualTo(1));
            Assert.That(registry.TryGet(player, out var after), Is.True);
            Assert.That(after.Inventory.SelectedQuickbarSlot, Is.EqualTo(3));
            Assert.That(tick.PlayerChanges.Count, Is.EqualTo(1));
        }

        [Test]
        public void SessionJsonStorage_RoundTripsInventoryAndSelection()
        {
            var path = Path.Combine(Path.GetTempPath(), "mf2-inventory-" + Guid.NewGuid().ToString("N"));
            try
            {
                var registry = new PlayerRegistry();
                using var world = new SimulationWorld();
                using var session = new GameSession(
                    registry, world, new CommandProcessor(), new SessionClock());
                session.Start();
                var player = new PlayerId(Guid.NewGuid());
                session.TryRegisterPlayer(player);
                session.SubmitCommand(player, GameCommand.ApplyInventory(
                    InventoryCommand.SelectQuickbar(2)));
                session.Step(new SimulationStep(1f / 30f));

                var storage = new SessionJsonStorage(path);
                storage.Save(session.CaptureArchive());
                var restored = storage.LoadCurrent();
                Assert.That(restored.Players[0].Inventory.SelectedQuickbarSlot, Is.EqualTo(2));
                Assert.That(
                    restored.Players[0].Inventory.GetSlot(InventoryContainerKind.Quickbar, 2).ItemId,
                    Is.EqualTo(ItemCatalog.Grass));
            }
            finally
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        [Test]
        public void NetworkEnvelope_RoundTripsWithoutClientSuppliedPlayerIdentity()
        {
            var source = InventoryCommand.Move(
                InventoryContainerKind.Quickbar, 0,
                InventoryContainerKind.Bag, 4, 3);
            var message = InventoryCommandBroadcast.FromCommand(source, 9);
            Assert.That(message.TryToCommand(out var restored), Is.True);
            Assert.That(restored.Kind, Is.EqualTo(InventoryCommandKind.Move));
            Assert.That(restored.SourceSlot, Is.Zero);
            Assert.That(restored.TargetSlot, Is.EqualTo(4));
            Assert.That(restored.Count, Is.EqualTo(3));
            Assert.That(typeof(InventoryCommandBroadcast).GetField("PlayerId"), Is.Null);
        }

        [Test]
        public void ClientOwnerInventory_IsGenerationAndRevisionGated()
        {
            using var client = new ClientStateCoordinator();
            var session = new SessionId(Guid.NewGuid());
            var player = new PlayerId(Guid.NewGuid());
            Assert.That(client.ApplyPlayerSnapshot(
                session, 2, 4, new[] { new PlayerModel(player) }), Is.True);
            var inventory = new InventoryState();
            Assert.That(client.ApplyLocalInventory(session, 2, 4, player, 1, inventory), Is.True);
            Assert.That(client.ApplyLocalInventory(session, 1, 5, player, 2, inventory), Is.False);
            Assert.That(client.ApplyLocalInventory(session, 2, 3, player, 2, inventory), Is.False);
            Assert.That(client.TryGetLocalInventory(out var owner, out var replica), Is.True);
            Assert.That(owner, Is.EqualTo(player));
            Assert.That(replica, Is.SameAs(inventory));
        }

        private static int Count(InventoryState inventory, uint id)
        {
            var total = 0;
            foreach (var container in new[]
                     {
                         InventoryContainerKind.Bag,
                         InventoryContainerKind.Quickbar,
                         InventoryContainerKind.Equipment
                     })
            {
                var slots = inventory.CaptureSlots(container);
                for (var i = 0; i < slots.Length; i++)
                    if (slots[i].ItemId == id)
                        total += slots[i].Count;
            }
            return total;
        }
    }
}
