using System;
using System.Runtime.InteropServices;

namespace MF2.World.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ChunkHandle : IEquatable<ChunkHandle>
    {
        public static readonly ChunkHandle Invalid = new ChunkHandle(-1, 0);

        public readonly int Index;
        public readonly uint Generation;

        internal ChunkHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid => Index >= 0 && Generation != 0;

        public bool Equals(ChunkHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397) ^ (int)Generation;
            }
        }

        public static bool operator ==(ChunkHandle left, ChunkHandle right) => left.Equals(right);
        public static bool operator !=(ChunkHandle left, ChunkHandle right) => !left.Equals(right);
    }
}
