using System;
using Unity.Mathematics;

namespace MF2.World.Core
{
    public enum WorldCommandType : byte
    {
        None = 0,
        SetBlock = 1,
        RemoveBlock = 2,
        FillRegion = 3
    }

    /// <summary>
    /// Unmanaged command payload. Fill bounds are inclusive and normalized on construction.
    /// </summary>
    public readonly struct WorldCommand
    {
        public readonly WorldCommandType Type;
        public readonly int3 Min;
        public readonly int3 Max;
        public readonly Block Block;

        private WorldCommand(WorldCommandType type, int3 min, int3 max, Block block)
        {
            Type = type;
            Min = min;
            Max = max;
            Block = block;
        }

        public static WorldCommand SetBlock(int3 position, Block block)
        {
            return new WorldCommand(WorldCommandType.SetBlock, position, position, block);
        }

        public static WorldCommand RemoveBlock(int3 position)
        {
            return new WorldCommand(WorldCommandType.RemoveBlock, position, position, Block.Air);
        }

        public static WorldCommand FillRegion(int3 first, int3 second, Block block)
        {
            return new WorldCommand(
                WorldCommandType.FillRegion,
                math.min(first, second),
                math.max(first, second),
                block);
        }

        public long ExpandedCellCount
        {
            get
            {
                if (Type != WorldCommandType.FillRegion)
                    return Type == WorldCommandType.None ? 0 : 1;

                var x = (ulong)((long)Max.x - Min.x + 1L);
                var y = (ulong)((long)Max.y - Min.y + 1L);
                var z = (ulong)((long)Max.z - Min.z + 1L);
                if (x > (ulong)long.MaxValue / y)
                    return long.MaxValue;

                var xy = x * y;
                if (xy > (ulong)long.MaxValue / z)
                    return long.MaxValue;

                return (long)(xy * z);
            }
        }
    }
}
