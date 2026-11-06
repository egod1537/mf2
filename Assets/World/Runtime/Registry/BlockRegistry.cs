using System;
using System.Collections.Generic;
using Unity.Collections;

namespace MF2.World.Registry
{
    public readonly struct BlockRegistryEntry
    {
        public readonly uint Id;
        public readonly int DenseIndex;
        public readonly FixedString128Bytes CanonicalName;

        internal BlockRegistryEntry(uint id, int denseIndex, FixedString128Bytes canonicalName)
        {
            Id = id;
            DenseIndex = denseIndex;
            CanonicalName = canonicalName;
        }
    }

    /// <summary>
    /// Disposable unmanaged runtime table. Stable IDs are external identity; DenseIndex is only
    /// a compact table index for this built registry and must never be persisted as a block ID.
    /// </summary>
    public sealed class BlockRegistry : IDisposable
    {
        private struct BuildEntry
        {
            public uint Id;
            public string Name;
        }

        private NativeArray<BlockRegistryEntry> entries;
        private NativeParallelHashMap<uint, int> idToDenseIndex;
        private bool disposed;

        public BlockRegistry(IReadOnlyList<string> canonicalNames, Allocator allocator = Allocator.Persistent)
        {
            if (canonicalNames == null)
                throw new ArgumentNullException(nameof(canonicalNames));

            var buildEntries = Validate(canonicalNames);
            entries = new NativeArray<BlockRegistryEntry>(
                buildEntries.Length,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            idToDenseIndex = new NativeParallelHashMap<uint, int>(
                Math.Max(1, buildEntries.Length),
                allocator);

            try
            {
                for (var i = 0; i < buildEntries.Length; i++)
                {
                    var buildEntry = buildEntries[i];
                    entries[i] = new BlockRegistryEntry(
                        buildEntry.Id,
                        i,
                        new FixedString128Bytes(buildEntry.Name));
                    idToDenseIndex.Add(buildEntry.Id, i);
                }
            }
            catch
            {
                if (idToDenseIndex.IsCreated)
                    idToDenseIndex.Dispose();
                if (entries.IsCreated)
                    entries.Dispose();
                throw;
            }
        }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return entries.Length;
            }
        }

        public NativeArray<BlockRegistryEntry> Entries
        {
            get
            {
                ThrowIfDisposed();
                return entries;
            }
        }

        public bool TryGetDenseIndex(uint stableId, out int denseIndex)
        {
            ThrowIfDisposed();
            if (stableId == 0)
            {
                denseIndex = -1;
                return false;
            }

            return idToDenseIndex.TryGetValue(stableId, out denseIndex);
        }

        public bool TryGetEntry(uint stableId, out BlockRegistryEntry entry)
        {
            if (TryGetDenseIndex(stableId, out var denseIndex))
            {
                entry = entries[denseIndex];
                return true;
            }

            entry = default;
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (idToDenseIndex.IsCreated)
                idToDenseIndex.Dispose();
            if (entries.IsCreated)
                entries.Dispose();
            disposed = true;
        }

        private static BuildEntry[] Validate(IReadOnlyList<string> canonicalNames)
        {
            var result = new BuildEntry[canonicalNames.Count];
            var namesById = new Dictionary<uint, string>(canonicalNames.Count);

            for (var i = 0; i < canonicalNames.Count; i++)
            {
                var name = canonicalNames[i];
                var id = StableBlockId.Compute(name);
                if (id == 0)
                {
                    throw new InvalidOperationException(
                        $"Canonical block name '{name}' hashes to reserved Air ID zero under " +
                        $"scheme {StableBlockId.SchemeVersion}.");
                }

                if (namesById.TryGetValue(id, out var existingName))
                {
                    var reason = string.Equals(existingName, name, StringComparison.Ordinal)
                        ? "duplicate canonical name"
                        : "xxHash32 collision";
                    throw new InvalidOperationException(
                        $"Block registry {reason}: '{existingName}' and '{name}' both map to " +
                        $"0x{id:X8}.");
                }

                namesById.Add(id, name);
                result[i] = new BuildEntry { Id = id, Name = name };
            }

            return result;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(BlockRegistry));
        }
    }
}
