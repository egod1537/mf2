using System;
using System.Runtime.InteropServices;

namespace MF2.World.Core
{
    /// <summary>
    /// The canonical unmanaged block payload. ID zero is Air and always has state zero.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public readonly struct Block : IEquatable<Block>
    {
        public static readonly Block Air = default;

        public readonly uint Id;
        public readonly uint State;

        public Block(uint id, uint state = 0)
        {
            if (id == 0 && state != 0)
                throw new ArgumentException("Air (ID zero) must have state zero.", nameof(state));

            Id = id;
            State = state;
        }

        public bool IsAir => Id == 0;

        public bool Equals(Block other)
        {
            return Id == other.Id && State == other.State;
        }

        public override bool Equals(object obj)
        {
            return obj is Block other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Id * 397) ^ (int)State;
            }
        }

        public static bool operator ==(Block left, Block right) => left.Equals(right);
        public static bool operator !=(Block left, Block right) => !left.Equals(right);
    }
}
