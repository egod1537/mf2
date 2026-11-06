using System;
using Unity.Mathematics;

namespace MF2.World.Core
{
    /// <summary>
    /// Signed world/chunk conversion for 16-cubed chunks.
    /// Linear indices are X-major: x + 16 * (y + 16 * z).
    /// </summary>
    public static class ChunkCoordinates
    {
        public const int Size = 16;
        public const int Area = Size * Size;
        public const int Volume = Size * Size * Size;

        public static int FloorDivide(int value)
        {
            var quotient = value / Size;
            var remainder = value % Size;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int FloorModulo(int value)
        {
            var remainder = value % Size;
            return remainder < 0 ? remainder + Size : remainder;
        }

        public static int3 ToChunk(int3 worldPosition)
        {
            return new int3(
                FloorDivide(worldPosition.x),
                FloorDivide(worldPosition.y),
                FloorDivide(worldPosition.z));
        }

        public static int3 ToLocal(int3 worldPosition)
        {
            return new int3(
                FloorModulo(worldPosition.x),
                FloorModulo(worldPosition.y),
                FloorModulo(worldPosition.z));
        }

        public static void Split(int3 worldPosition, out int3 chunk, out int3 local)
        {
            chunk = ToChunk(worldPosition);
            local = worldPosition - chunk * Size;
        }

        public static int ToIndex(int3 localPosition)
        {
            if ((uint)localPosition.x >= Size ||
                (uint)localPosition.y >= Size ||
                (uint)localPosition.z >= Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPosition),
                    "Local block coordinates must be in the inclusive range 0..15.");
            }

            return localPosition.x + Size * (localPosition.y + Size * localPosition.z);
        }

        public static int3 FromIndex(int index)
        {
            if ((uint)index >= Volume)
                throw new ArgumentOutOfRangeException(nameof(index));

            var x = index % Size;
            var yz = index / Size;
            var y = yz % Size;
            var z = yz / Size;
            return new int3(x, y, z);
        }
    }
}
