using System;
using System.Collections.Generic;
using System.IO;
using MF2.World.Core;
using Unity.Mathematics;

namespace MF2.World.Storage
{
    public readonly struct StoredBlock : IEquatable<StoredBlock>
    {
        public readonly int Index;
        public readonly uint Id;
        public readonly uint State;

        public StoredBlock(int index, uint id, uint state)
        {
            if ((uint)index >= ChunkCoordinates.Volume)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (id == 0)
                throw new ArgumentException("Sparse storage must not contain Air entries.", nameof(id));

            Index = index;
            Id = id;
            State = state;
        }

        public Block ToBlock()
        {
            return new Block(Id, State);
        }

        public bool Equals(StoredBlock other)
        {
            return Index == other.Index && Id == other.Id && State == other.State;
        }

        public override bool Equals(object obj)
        {
            return obj is StoredBlock other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Index * 397) ^ (int)Id) * 397 ^ (int)State;
            }
        }
    }

    /// <summary>
    /// Managed, sparse chunk data used at the persistence seam. It contains no native memory,
    /// Unity object reference, renderer, or mesh data.
    /// </summary>
    public sealed class StoredChunk
    {
        private readonly StoredBlock[] blocks;

        public StoredChunk(int3 chunkCoordinate, IReadOnlyList<StoredBlock> sourceBlocks)
        {
            if (sourceBlocks == null)
                throw new ArgumentNullException(nameof(sourceBlocks));

            RegionCoordinates.ValidateWorldChunkRange(chunkCoordinate);
            ChunkCoordinate = chunkCoordinate;
            blocks = new StoredBlock[sourceBlocks.Count];
            var seen = new HashSet<int>();
            for (var i = 0; i < sourceBlocks.Count; i++)
            {
                var block = sourceBlocks[i];
                if ((uint)block.Index >= ChunkCoordinates.Volume || block.Id == 0)
                {
                    throw new ArgumentException(
                        "Stored chunks may contain only non-Air entries with indices 0..4095.",
                        nameof(sourceBlocks));
                }

                if (!seen.Add(block.Index))
                {
                    throw new ArgumentException(
                        $"Stored chunk contains duplicate block index {block.Index}.",
                        nameof(sourceBlocks));
                }

                blocks[i] = block;
            }

            Array.Sort(blocks, (left, right) => left.Index.CompareTo(right.Index));
        }

        public int3 ChunkCoordinate { get; }
        public IReadOnlyList<StoredBlock> Blocks => blocks;
        public bool IsEmpty => blocks.Length == 0;
    }

    public sealed class WorldStorageSnapshot
    {
        private readonly StoredChunk[] chunks;

        public WorldStorageSnapshot(IReadOnlyList<StoredChunk> sourceChunks)
        {
            if (sourceChunks == null)
                throw new ArgumentNullException(nameof(sourceChunks));

            chunks = new StoredChunk[sourceChunks.Count];
            var seen = new HashSet<int3>();
            for (var i = 0; i < sourceChunks.Count; i++)
            {
                var chunk = sourceChunks[i] ??
                            throw new ArgumentException("A world snapshot cannot contain null chunks.");
                if (!seen.Add(chunk.ChunkCoordinate))
                {
                    throw new ArgumentException(
                        $"World snapshot contains duplicate chunk {chunk.ChunkCoordinate}.",
                        nameof(sourceChunks));
                }

                chunks[i] = chunk;
            }

            Array.Sort(chunks, (left, right) => CompareCoordinates(
                left.ChunkCoordinate,
                right.ChunkCoordinate));
        }

        public IReadOnlyList<StoredChunk> Chunks => chunks;
        public int ChunkCount => chunks.Length;

        private static int CompareCoordinates(int3 left, int3 right)
        {
            var x = left.x.CompareTo(right.x);
            if (x != 0)
                return x;
            var y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.z.CompareTo(right.z);
        }
    }

    public enum RecoveryArtifactKind : byte
    {
        Temporary,
        Backup
    }

    public readonly struct RecoveryArtifact
    {
        public readonly RecoveryArtifactKind Kind;
        public readonly string Path;
        public readonly string MainPath;
        public readonly bool MainFileExists;

        internal RecoveryArtifact(
            RecoveryArtifactKind kind,
            string path,
            string mainPath,
            bool mainFileExists)
        {
            Kind = kind;
            Path = path;
            MainPath = mainPath;
            MainFileExists = mainFileExists;
        }
    }

    public sealed class WorldStorageException : IOException
    {
        public WorldStorageException(string message)
            : base(message)
        {
        }

        public WorldStorageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
