using System;
using System.Collections.Generic;
using Unity.Collections;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Unmanaged render lookup built once from the authoring manifest. Stable block IDs remain
    /// external identity; the hash map only selects a dense render definition.
    /// </summary>
    public sealed class BlockRenderTable : IDisposable
    {
        private NativeArray<BlockRenderDefinition> definitions;
        private NativeParallelHashMap<uint, int> idToIndex;
        private bool disposed;

        public BlockRenderTable(
            IReadOnlyList<BlockRenderDefinition> source,
            Allocator allocator = Allocator.Persistent)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            definitions = new NativeArray<BlockRenderDefinition>(
                source.Count,
                allocator,
                NativeArrayOptions.UninitializedMemory);
            idToIndex = new NativeParallelHashMap<uint, int>(Math.Max(1, source.Count), allocator);
            try
            {
                for (var i = 0; i < source.Count; i++)
                {
                    var definition = source[i];
                    if (definition.Id == 0)
                        throw new InvalidOperationException("Air cannot have a render definition.");
                    if (!idToIndex.TryAdd(definition.Id, i))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate render definition for stable ID 0x{definition.Id:X8}.");
                    }

                    definitions[i] = definition;
                }
            }
            catch
            {
                if (idToIndex.IsCreated)
                    idToIndex.Dispose();
                if (definitions.IsCreated)
                    definitions.Dispose();
                throw;
            }
        }

        public int Count
        {
            get
            {
                ThrowIfDisposed();
                return definitions.Length;
            }
        }

        public NativeArray<BlockRenderDefinition>.ReadOnly Definitions
        {
            get
            {
                ThrowIfDisposed();
                return definitions.AsReadOnly();
            }
        }

        public NativeParallelHashMap<uint, int>.ReadOnly IdToIndex
        {
            get
            {
                ThrowIfDisposed();
                return idToIndex.AsReadOnly();
            }
        }

        public bool TryGetDefinition(uint stableId, out BlockRenderDefinition definition)
        {
            ThrowIfDisposed();
            if (idToIndex.TryGetValue(stableId, out var index))
            {
                definition = definitions[index];
                return true;
            }

            definition = default;
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (idToIndex.IsCreated)
                idToIndex.Dispose();
            if (definitions.IsCreated)
                definitions.Dispose();
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(BlockRenderTable));
        }
    }
}
