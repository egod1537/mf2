using System;
using MF2.World.Core;
using MF2.World.Registry;

namespace MF2.Game.Domain
{
    /// <summary>Validated, finite bootstrap bounds for the authoritative block world.</summary>
    public readonly struct TerrainConfig : IEquatable<TerrainConfig>
    {
        public const long DefaultMaximumBlockBudget = 1_000_000;

        public TerrainConfig(
            int originX,
            int originZ,
            int width,
            int depth,
            int minimumY,
            int maximumY,
            int baseHeight,
            float amplitude,
            float frequency,
            int octaves,
            int seed,
            Block solidBlock,
            long maximumBlockBudget = DefaultMaximumBlockBudget)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (depth <= 0)
                throw new ArgumentOutOfRangeException(nameof(depth));
            if (maximumY < minimumY)
                throw new ArgumentOutOfRangeException(nameof(maximumY));
            if (baseHeight < minimumY || baseHeight > maximumY)
                throw new ArgumentOutOfRangeException(nameof(baseHeight));
            if (float.IsNaN(amplitude) || float.IsInfinity(amplitude) || amplitude <= 0f)
                throw new ArgumentOutOfRangeException(nameof(amplitude));
            if (float.IsNaN(frequency) || float.IsInfinity(frequency) || frequency <= 0f)
                throw new ArgumentOutOfRangeException(nameof(frequency));
            if (octaves <= 0 || octaves > 8)
                throw new ArgumentOutOfRangeException(nameof(octaves));
            if (solidBlock.IsAir)
                throw new ArgumentException("Generated terrain requires a non-air block.", nameof(solidBlock));
            if (maximumBlockBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBlockBudget));

            _ = checked(originX + width - 1);
            _ = checked(originZ + depth - 1);
            var horizontalCells = checked((long)width * depth);
            var verticalCells = checked((long)maximumY - minimumY + 1L);
            var possibleBlocks = checked(horizontalCells * verticalCells);
            if (possibleBlocks > maximumBlockBudget)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumBlockBudget),
                    $"Terrain bounds may contain {possibleBlocks} blocks, exceeding the " +
                    $"configured budget of {maximumBlockBudget}.");
            }

            // The configured wave must be able to vary at least one rounded block height.
            if (amplitude < 0.5f && minimumY != maximumY)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amplitude),
                    "Amplitude must be at least half a block for a non-flat height range.");
            }

            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Depth = depth;
            MinimumY = minimumY;
            MaximumY = maximumY;
            BaseHeight = baseHeight;
            Amplitude = amplitude;
            Frequency = frequency;
            Octaves = octaves;
            Seed = seed;
            SolidBlock = solidBlock;
            MaximumBlockBudget = maximumBlockBudget;
        }

        public int OriginX { get; }
        public int OriginZ { get; }
        public int Width { get; }
        public int Depth { get; }
        public int MinimumY { get; }
        public int MaximumY { get; }
        public int BaseHeight { get; }
        public float Amplitude { get; }
        public float Frequency { get; }
        public int Octaves { get; }
        public int Seed { get; }
        public Block SolidBlock { get; }
        public long MaximumBlockBudget { get; }
        public int MaximumX => checked(OriginX + Width - 1);
        public int MaximumZ => checked(OriginZ + Depth - 1);

        public bool ContainsColumn(int x, int z) =>
            x >= OriginX && x <= MaximumX && z >= OriginZ && z <= MaximumZ;

        public static TerrainConfig CreateDefault() => new TerrainConfig(
            originX: -32,
            originZ: -32,
            width: 64,
            depth: 64,
            minimumY: 0,
            maximumY: 16,
            baseHeight: 8,
            amplitude: 5f,
            frequency: 0.025f,
            octaves: 3,
            seed: 12345,
            solidBlock: new Block(StableBlockId.Compute("minecraft:stone")));

        public bool Equals(TerrainConfig other) =>
            OriginX == other.OriginX && OriginZ == other.OriginZ &&
            Width == other.Width && Depth == other.Depth &&
            MinimumY == other.MinimumY && MaximumY == other.MaximumY &&
            BaseHeight == other.BaseHeight && Amplitude.Equals(other.Amplitude) &&
            Frequency.Equals(other.Frequency) && Octaves == other.Octaves &&
            Seed == other.Seed && SolidBlock == other.SolidBlock &&
            MaximumBlockBudget == other.MaximumBlockBudget;

        public override bool Equals(object obj) => obj is TerrainConfig other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = OriginX;
                hash = hash * 397 ^ OriginZ;
                hash = hash * 397 ^ Width;
                hash = hash * 397 ^ Depth;
                hash = hash * 397 ^ MinimumY;
                hash = hash * 397 ^ MaximumY;
                hash = hash * 397 ^ BaseHeight;
                hash = hash * 397 ^ Amplitude.GetHashCode();
                hash = hash * 397 ^ Frequency.GetHashCode();
                hash = hash * 397 ^ Octaves;
                hash = hash * 397 ^ Seed;
                hash = hash * 397 ^ SolidBlock.GetHashCode();
                return hash * 397 ^ MaximumBlockBudget.GetHashCode();
            }
        }
    }
}
