using System;
using Unity.Collections;
using Unity.Mathematics;

namespace MF2.World.Core
{
    internal sealed class ChunkPool : IDisposable
    {
        private struct SlotMetadata
        {
            public int3 Coordinate;
            public ulong Version;
            public uint Generation;
            public int LeaseCount;
            public byte Occupied;
        }

        private NativeList<Block> blocks;
        private NativeList<SlotMetadata> slots;
        private NativeList<int> freeSlots;
        private NativeParallelHashMap<ulong, ChunkHandle> activeLeaseTokens;
        private int nextUnusedIndex;
        private int occupiedCount;
        private ulong nextLeaseToken;
        private readonly Allocator allocator;

        public ChunkPool(int initialCapacity, Allocator allocator)
        {
            if (initialCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            this.allocator = allocator;
            blocks = new NativeList<Block>(initialCapacity * ChunkCoordinates.Volume, allocator);
            slots = new NativeList<SlotMetadata>(initialCapacity, allocator);
            freeSlots = new NativeList<int>(initialCapacity, allocator);
            activeLeaseTokens = new NativeParallelHashMap<ulong, ChunkHandle>(initialCapacity, allocator);

            slots.Resize(initialCapacity, NativeArrayOptions.ClearMemory);
            blocks.Resize(initialCapacity * ChunkCoordinates.Volume, NativeArrayOptions.ClearMemory);
            nextUnusedIndex = 0;
            occupiedCount = 0;
            nextLeaseToken = 1;
        }

        public int Capacity => slots.IsCreated ? slots.Length : 0;
        public int OccupiedCount => occupiedCount;
        public int ActiveLeaseCount => activeLeaseTokens.IsCreated ? activeLeaseTokens.Count() : 0;

        public ChunkHandle Acquire(int3 coordinate)
        {
            int index;
            if (freeSlots.Length > 0)
            {
                var last = freeSlots.Length - 1;
                index = freeSlots[last];
                freeSlots.RemoveAtSwapBack(last);
            }
            else
            {
                if (nextUnusedIndex == slots.Length)
                    Grow();
                index = nextUnusedIndex++;
            }

            var metadata = slots[index];
            metadata.Generation = NextGeneration(metadata.Generation);
            metadata.Coordinate = coordinate;
            metadata.Version = 0;
            metadata.LeaseCount = 0;
            metadata.Occupied = 1;
            slots[index] = metadata;
            ClearSlot(index);
            occupiedCount++;
            return new ChunkHandle(index, metadata.Generation);
        }

        public bool Release(ChunkHandle handle)
        {
            if (!TryGetMetadata(handle, out var metadata) || metadata.LeaseCount != 0)
                return false;

            ClearSlot(handle.Index);
            metadata.Coordinate = default;
            metadata.Version = 0;
            metadata.Occupied = 0;
            slots[handle.Index] = metadata;
            freeSlots.Add(handle.Index);
            occupiedCount--;
            return true;
        }

        public bool IsValid(ChunkHandle handle)
        {
            return TryGetMetadata(handle, out _);
        }

        public bool TryGetCoordinate(ChunkHandle handle, out int3 coordinate)
        {
            if (TryGetMetadata(handle, out var metadata))
            {
                coordinate = metadata.Coordinate;
                return true;
            }

            coordinate = default;
            return false;
        }

        public bool TryGetBlock(ChunkHandle handle, int index, out Block block)
        {
            if (!IsValid(handle) || (uint)index >= ChunkCoordinates.Volume)
            {
                block = default;
                return false;
            }

            block = blocks[handle.Index * ChunkCoordinates.Volume + index];
            return true;
        }

        public bool TrySetBlock(ChunkHandle handle, int index, Block block)
        {
            if (!IsValid(handle) || (uint)index >= ChunkCoordinates.Volume)
                return false;

            blocks[handle.Index * ChunkCoordinates.Volume + index] = block;
            return true;
        }

        public bool TryAcquireLease(ChunkHandle handle, out ChunkLease lease)
        {
            if (!TryGetMetadata(handle, out var metadata))
            {
                lease = default;
                return false;
            }

            EnsureLeaseCapacity();
            var token = AllocateLeaseToken();
            activeLeaseTokens.Add(token, handle);
            metadata.LeaseCount++;
            slots[handle.Index] = metadata;

            lease = new ChunkLease(
                handle,
                metadata.Coordinate,
                blocks.AsReadOnly(),
                handle.Index * ChunkCoordinates.Volume,
                token);
            return true;
        }

        public bool ReleaseLease(in ChunkLease lease)
        {
            if (lease.Token == 0 ||
                !activeLeaseTokens.TryGetValue(lease.Token, out var leasedHandle) ||
                leasedHandle != lease.Handle ||
                !TryGetMetadata(lease.Handle, out var metadata))
            {
                return false;
            }

            activeLeaseTokens.Remove(lease.Token);
            metadata.LeaseCount--;
            slots[lease.Handle.Index] = metadata;
            return true;
        }

        public ulong IncrementVersion(ChunkHandle handle)
        {
            if (!TryGetMetadata(handle, out var metadata))
                throw new InvalidOperationException("Cannot version a stale chunk handle.");
            if (metadata.Version == ulong.MaxValue)
                throw new OverflowException("Chunk version exhausted its monotonic range.");

            metadata.Version++;
            slots[handle.Index] = metadata;
            return metadata.Version;
        }

        public bool IsVersionCurrent(ChunkHandle handle, ulong expectedVersion)
        {
            return TryGetMetadata(handle, out var metadata) && metadata.Version == expectedVersion;
        }

        public bool TryGetVersion(ChunkHandle handle, out ulong version)
        {
            if (TryGetMetadata(handle, out var metadata))
            {
                version = metadata.Version;
                return true;
            }

            version = 0;
            return false;
        }

        public void Dispose()
        {
            if (activeLeaseTokens.IsCreated)
                activeLeaseTokens.Dispose();
            if (freeSlots.IsCreated)
                freeSlots.Dispose();
            if (slots.IsCreated)
                slots.Dispose();
            if (blocks.IsCreated)
                blocks.Dispose();

            nextUnusedIndex = 0;
            occupiedCount = 0;
        }

        private bool TryGetMetadata(ChunkHandle handle, out SlotMetadata metadata)
        {
            if (!handle.IsValid || !slots.IsCreated || (uint)handle.Index >= slots.Length)
            {
                metadata = default;
                return false;
            }

            metadata = slots[handle.Index];
            return metadata.Occupied != 0 && metadata.Generation == handle.Generation;
        }

        private void Grow()
        {
            if (ActiveLeaseCount != 0)
                throw new InvalidOperationException("Cannot resize the chunk pool while a lease is active.");

            var oldCapacity = slots.Length;
            var newCapacity = checked(oldCapacity * 2);
            slots.Resize(newCapacity, NativeArrayOptions.ClearMemory);
            blocks.Resize(
                checked(newCapacity * ChunkCoordinates.Volume),
                NativeArrayOptions.ClearMemory);

            if (freeSlots.Capacity < newCapacity)
                freeSlots.Capacity = newCapacity;
            if (activeLeaseTokens.Capacity < newCapacity)
                activeLeaseTokens.Capacity = newCapacity;
        }

        private void ClearSlot(int index)
        {
            var start = index * ChunkCoordinates.Volume;
            for (var i = 0; i < ChunkCoordinates.Volume; i++)
                blocks[start + i] = default;
        }

        private void EnsureLeaseCapacity()
        {
            if (activeLeaseTokens.Count() < activeLeaseTokens.Capacity)
                return;

            activeLeaseTokens.Capacity = Math.Max(1, activeLeaseTokens.Capacity * 2);
        }

        private ulong AllocateLeaseToken()
        {
            while (nextLeaseToken == 0 || activeLeaseTokens.ContainsKey(nextLeaseToken))
                nextLeaseToken++;

            return nextLeaseToken++;
        }

        private static uint NextGeneration(uint current)
        {
            if (current == uint.MaxValue)
                throw new OverflowException("Chunk generation exhausted its monotonic range.");
            return current + 1;
        }
    }
}
