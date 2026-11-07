using System;
using MF2.World.Registry;

namespace MF2.Game.Domain
{
    /// <summary>Process-local presentation gate; never part of authoritative state or persistence.</summary>
    public static class GameplayInputGate
    {
        public static bool IsInventoryOpen { get; set; }
        public static bool IsPointerOverInterface { get; set; }
    }

    public enum InventoryContainerKind : byte
    {
        Bag,
        Quickbar,
        Equipment
    }

    public readonly struct ItemStack : IEquatable<ItemStack>
    {
        public ItemStack(uint itemId, int count)
        {
            if (!ItemCatalog.IsKnown(itemId))
                throw new ArgumentOutOfRangeException(nameof(itemId), "Unknown item id.");
            if (count <= 0 || count > ItemCatalog.GetMaximumStack(itemId))
                throw new ArgumentOutOfRangeException(nameof(count));
            ItemId = itemId;
            Count = count;
        }

        public uint ItemId { get; }
        public int Count { get; }
        public bool IsEmpty => ItemId == 0;
        public static ItemStack Empty => default;

        public bool Equals(ItemStack other) => ItemId == other.ItemId && Count == other.Count;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ItemId, Count);
        public static bool operator ==(ItemStack left, ItemStack right) => left.Equals(right);
        public static bool operator !=(ItemStack left, ItemStack right) => !left.Equals(right);
    }

    /// <summary>Small deterministic item catalog. Item ids intentionally reuse stable block ids.</summary>
    public static class ItemCatalog
    {
        public static readonly uint Stone = StableBlockId.Compute("minecraft:stone");
        public static readonly uint Dirt = StableBlockId.Compute("minecraft:dirt");
        public static readonly uint Grass = StableBlockId.Compute("minecraft:grass_block");
        public static readonly uint Water = StableBlockId.Compute("minecraft:water");

        public static bool IsKnown(uint id) =>
            id == Stone || id == Dirt || id == Grass || id == Water;

        public static int GetMaximumStack(uint id)
        {
            if (!IsKnown(id))
                throw new ArgumentOutOfRangeException(nameof(id));
            return 64;
        }
    }

    public enum InventoryCommandKind : byte
    {
        None,
        SelectQuickbar,
        Move
    }

    public readonly struct InventoryCommand
    {
        private InventoryCommand(
            InventoryCommandKind kind,
            InventoryContainerKind sourceContainer,
            int sourceSlot,
            InventoryContainerKind targetContainer,
            int targetSlot,
            int count,
            int selectedQuickbarSlot)
        {
            Kind = kind;
            SourceContainer = sourceContainer;
            SourceSlot = sourceSlot;
            TargetContainer = targetContainer;
            TargetSlot = targetSlot;
            Count = count;
            SelectedQuickbarSlot = selectedQuickbarSlot;
        }

        public InventoryCommandKind Kind { get; }
        public InventoryContainerKind SourceContainer { get; }
        public int SourceSlot { get; }
        public InventoryContainerKind TargetContainer { get; }
        public int TargetSlot { get; }
        public int Count { get; }
        public int SelectedQuickbarSlot { get; }

        public static InventoryCommand SelectQuickbar(int slot) => new InventoryCommand(
            InventoryCommandKind.SelectQuickbar,
            default, 0, default, 0, 0, slot);

        public static InventoryCommand Move(
            InventoryContainerKind sourceContainer,
            int sourceSlot,
            InventoryContainerKind targetContainer,
            int targetSlot,
            int count) => new InventoryCommand(
                InventoryCommandKind.Move,
                sourceContainer,
                sourceSlot,
                targetContainer,
                targetSlot,
                count,
                0);

        public bool IsStructurallyValid => Kind switch
        {
            InventoryCommandKind.SelectQuickbar =>
                SelectedQuickbarSlot >= 0 &&
                SelectedQuickbarSlot < InventoryState.QuickbarSlotCount,
            InventoryCommandKind.Move =>
                Count > 0 &&
                InventoryState.IsValidSlot(SourceContainer, SourceSlot) &&
                InventoryState.IsValidSlot(TargetContainer, TargetSlot) &&
                (SourceContainer != TargetContainer || SourceSlot != TargetSlot),
            _ => false
        };
    }

    /// <summary>Immutable inventory snapshot owned by PlayerRegistry.</summary>
    public sealed class InventoryState : IEquatable<InventoryState>
    {
        public const int BagSlotCount = 30;
        public const int QuickbarSlotCount = 7;
        public const int EquipmentSlotCount = 6;

        private readonly ItemStack[] bag;
        private readonly ItemStack[] quickbar;
        private readonly ItemStack[] equipment;

        public InventoryState()
            : this(
                new ItemStack[BagSlotCount],
                CreateDefaultQuickbar(),
                new ItemStack[EquipmentSlotCount],
                0)
        {
        }

        public InventoryState(
            ItemStack[] bag,
            ItemStack[] quickbar,
            ItemStack[] equipment,
            int selectedQuickbarSlot)
        {
            this.bag = CopyAndValidate(bag, BagSlotCount, nameof(bag));
            this.quickbar = CopyAndValidate(quickbar, QuickbarSlotCount, nameof(quickbar));
            this.equipment = CopyAndValidate(equipment, EquipmentSlotCount, nameof(equipment));
            if (selectedQuickbarSlot < 0 || selectedQuickbarSlot >= QuickbarSlotCount)
                throw new ArgumentOutOfRangeException(nameof(selectedQuickbarSlot));
            SelectedQuickbarSlot = selectedQuickbarSlot;
        }

        public int SelectedQuickbarSlot { get; }
        public ItemStack SelectedStack => quickbar[SelectedQuickbarSlot];

        public ItemStack GetSlot(InventoryContainerKind container, int slot)
        {
            if (!IsValidSlot(container, slot))
                throw new ArgumentOutOfRangeException(nameof(slot));
            return GetArray(container)[slot];
        }

        public ItemStack[] CaptureSlots(InventoryContainerKind container) =>
            (ItemStack[])GetArray(container).Clone();

        public bool TryApply(in InventoryCommand command, out InventoryState result)
        {
            result = this;
            if (!command.IsStructurallyValid)
                return false;
            if (command.Kind == InventoryCommandKind.SelectQuickbar)
            {
                if (SelectedQuickbarSlot == command.SelectedQuickbarSlot)
                    return true;
                result = new InventoryState(
                    bag, quickbar, equipment, command.SelectedQuickbarSlot);
                return true;
            }

            var source = GetSlot(command.SourceContainer, command.SourceSlot);
            var target = GetSlot(command.TargetContainer, command.TargetSlot);
            if (source.IsEmpty || command.Count > source.Count ||
                command.TargetContainer == InventoryContainerKind.Equipment)
            {
                return false;
            }

            var nextBag = (ItemStack[])bag.Clone();
            var nextQuickbar = (ItemStack[])quickbar.Clone();
            var nextEquipment = (ItemStack[])equipment.Clone();
            var sourceArray = GetArray(command.SourceContainer, nextBag, nextQuickbar, nextEquipment);
            var targetArray = GetArray(command.TargetContainer, nextBag, nextQuickbar, nextEquipment);

            if (target.IsEmpty)
            {
                targetArray[command.TargetSlot] = new ItemStack(source.ItemId, command.Count);
                sourceArray[command.SourceSlot] = source.Count == command.Count
                    ? ItemStack.Empty
                    : new ItemStack(source.ItemId, source.Count - command.Count);
            }
            else if (target.ItemId == source.ItemId)
            {
                var combined = checked(target.Count + command.Count);
                if (combined > ItemCatalog.GetMaximumStack(target.ItemId))
                    return false;
                targetArray[command.TargetSlot] = new ItemStack(target.ItemId, combined);
                sourceArray[command.SourceSlot] = source.Count == command.Count
                    ? ItemStack.Empty
                    : new ItemStack(source.ItemId, source.Count - command.Count);
            }
            else
            {
                if (command.Count != source.Count ||
                    command.SourceContainer == InventoryContainerKind.Equipment)
                    return false;
                sourceArray[command.SourceSlot] = target;
                targetArray[command.TargetSlot] = source;
            }

            result = new InventoryState(
                nextBag, nextQuickbar, nextEquipment, SelectedQuickbarSlot);
            return true;
        }

        public bool Equals(InventoryState other)
        {
            if (ReferenceEquals(null, other) || SelectedQuickbarSlot != other.SelectedQuickbarSlot)
                return false;
            return ArraysEqual(bag, other.bag) &&
                   ArraysEqual(quickbar, other.quickbar) &&
                   ArraysEqual(equipment, other.equipment);
        }

        public override bool Equals(object obj) => obj is InventoryState other && Equals(other);

        public override int GetHashCode()
        {
            var hash = SelectedQuickbarSlot;
            for (var i = 0; i < quickbar.Length; i++)
                hash = HashCode.Combine(hash, quickbar[i]);
            return hash;
        }

        public static bool IsValidSlot(InventoryContainerKind container, int slot) =>
            slot >= 0 && slot < GetSlotCount(container);

        public static int GetSlotCount(InventoryContainerKind container) => container switch
        {
            InventoryContainerKind.Bag => BagSlotCount,
            InventoryContainerKind.Quickbar => QuickbarSlotCount,
            InventoryContainerKind.Equipment => EquipmentSlotCount,
            _ => 0
        };

        private ItemStack[] GetArray(InventoryContainerKind container) => container switch
        {
            InventoryContainerKind.Bag => bag,
            InventoryContainerKind.Quickbar => quickbar,
            InventoryContainerKind.Equipment => equipment,
            _ => throw new ArgumentOutOfRangeException(nameof(container))
        };

        private static ItemStack[] GetArray(
            InventoryContainerKind container,
            ItemStack[] bag,
            ItemStack[] quickbar,
            ItemStack[] equipment) => container switch
        {
            InventoryContainerKind.Bag => bag,
            InventoryContainerKind.Quickbar => quickbar,
            InventoryContainerKind.Equipment => equipment,
            _ => throw new ArgumentOutOfRangeException(nameof(container))
        };

        private static ItemStack[] CopyAndValidate(ItemStack[] source, int length, string name)
        {
            if (source == null || source.Length != length)
                throw new ArgumentException($"Expected {length} slots.", name);
            var copy = (ItemStack[])source.Clone();
            for (var i = 0; i < copy.Length; i++)
            {
                var stack = copy[i];
                if (!stack.IsEmpty &&
                    (!ItemCatalog.IsKnown(stack.ItemId) || stack.Count <= 0 ||
                     stack.Count > ItemCatalog.GetMaximumStack(stack.ItemId)))
                    throw new ArgumentException("Inventory contains an invalid stack.", name);
            }
            return copy;
        }

        private static ItemStack[] CreateDefaultQuickbar()
        {
            var slots = new ItemStack[QuickbarSlotCount];
            slots[0] = new ItemStack(ItemCatalog.Stone, 64);
            slots[1] = new ItemStack(ItemCatalog.Dirt, 64);
            slots[2] = new ItemStack(ItemCatalog.Grass, 64);
            slots[3] = new ItemStack(ItemCatalog.Water, 64);
            return slots;
        }

        private static bool ArraysEqual(ItemStack[] left, ItemStack[] right)
        {
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }
    }
}
