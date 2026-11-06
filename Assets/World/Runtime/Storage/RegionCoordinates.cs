using System;
using System.Globalization;
using MF2.World.Core;
using Unity.Mathematics;

namespace MF2.World.Storage
{
    /// <summary>
    /// Signed chunk/region conversion. A region is 16 cubed chunks, or 256 cubed blocks.
    /// Negative coordinates use mathematical floor division: chunk -1 is region -1, local 15.
    /// </summary>
    public static class RegionCoordinates
    {
        public const int ChunksPerAxis = 16;
        public const int BlocksPerAxis = ChunkCoordinates.Size * ChunksPerAxis;

        public static int FloorDivide(int chunkCoordinate)
        {
            var quotient = chunkCoordinate / ChunksPerAxis;
            var remainder = chunkCoordinate % ChunksPerAxis;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int FloorModulo(int chunkCoordinate)
        {
            var remainder = chunkCoordinate % ChunksPerAxis;
            return remainder < 0 ? remainder + ChunksPerAxis : remainder;
        }

        public static int3 ToRegion(int3 chunkCoordinate)
        {
            return new int3(
                FloorDivide(chunkCoordinate.x),
                FloorDivide(chunkCoordinate.y),
                FloorDivide(chunkCoordinate.z));
        }

        public static int3 ToLocalChunk(int3 chunkCoordinate)
        {
            return new int3(
                FloorModulo(chunkCoordinate.x),
                FloorModulo(chunkCoordinate.y),
                FloorModulo(chunkCoordinate.z));
        }

        public static int3 ComposeChunk(int3 regionCoordinate, int3 localChunk)
        {
            ValidateLocalChunk(localChunk);
            return new int3(
                ComposeAxis(regionCoordinate.x, localChunk.x),
                ComposeAxis(regionCoordinate.y, localChunk.y),
                ComposeAxis(regionCoordinate.z, localChunk.z));
        }

        public static string GetFileName(int3 regionCoordinate)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "r.{0}.{1}.{2}.json",
                regionCoordinate.x,
                regionCoordinate.y,
                regionCoordinate.z);
        }

        public static bool TryParseFileName(string fileName, out int3 regionCoordinate)
        {
            regionCoordinate = default;
            if (string.IsNullOrEmpty(fileName) ||
                !fileName.EndsWith(".json", StringComparison.Ordinal))
            {
                return false;
            }

            var stem = fileName.Substring(0, fileName.Length - ".json".Length);
            var parts = stem.Split('.');
            if (parts.Length != 4 || !string.Equals(parts[0], "r", StringComparison.Ordinal))
                return false;

            if (!int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) ||
                !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y) ||
                !int.TryParse(parts[3], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var z))
            {
                return false;
            }

            regionCoordinate = new int3(x, y, z);
            return true;
        }

        internal static void ValidateLocalChunk(int3 localChunk)
        {
            if ((uint)localChunk.x >= ChunksPerAxis ||
                (uint)localChunk.y >= ChunksPerAxis ||
                (uint)localChunk.z >= ChunksPerAxis)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localChunk),
                    "Region-local chunk coordinates must be in the inclusive range 0..15.");
            }
        }

        internal static void ValidateWorldChunkRange(int3 chunkCoordinate)
        {
            ValidateWorldChunkAxis(chunkCoordinate.x, nameof(chunkCoordinate));
            ValidateWorldChunkAxis(chunkCoordinate.y, nameof(chunkCoordinate));
            ValidateWorldChunkAxis(chunkCoordinate.z, nameof(chunkCoordinate));
        }

        internal static int3 ComposeWorldPosition(int3 chunkCoordinate, int localIndex)
        {
            ValidateWorldChunkRange(chunkCoordinate);
            var local = ChunkCoordinates.FromIndex(localIndex);
            return new int3(
                checked(chunkCoordinate.x * ChunkCoordinates.Size + local.x),
                checked(chunkCoordinate.y * ChunkCoordinates.Size + local.y),
                checked(chunkCoordinate.z * ChunkCoordinates.Size + local.z));
        }

        private static int ComposeAxis(int regionCoordinate, int localChunk)
        {
            var value = (long)regionCoordinate * ChunksPerAxis + localChunk;
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new OverflowException(
                    "Region and local chunk coordinates compose outside signed Int32 range.");
            }

            return (int)value;
        }

        private static void ValidateWorldChunkAxis(int chunkCoordinate, string parameterName)
        {
            var firstBlock = (long)chunkCoordinate * ChunkCoordinates.Size;
            var lastBlock = firstBlock + ChunkCoordinates.Size - 1L;
            if (firstBlock < int.MinValue || lastBlock > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Chunk coordinate cannot be represented by signed Int32 block coordinates.");
            }
        }
    }
}
