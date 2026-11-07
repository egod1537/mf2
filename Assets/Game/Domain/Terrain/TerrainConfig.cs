using System;
using MF2.World.Core;
using MF2.World.Registry;

namespace MF2.Game.Domain
{
    public enum TerrainGenerationProfile : byte
    {
        Normal,
        Amplified
    }

    /// <summary>Stable block palette used by layered terrain generation.</summary>
    public readonly struct TerrainSurfacePalette : IEquatable<TerrainSurfacePalette>
    {
        public TerrainSurfacePalette(Block stone, Block dirt, Block grass, Block water)
        {
            Stone = stone;
            Dirt = dirt;
            Grass = grass;
            Water = water;
        }

        public Block Stone { get; }
        public Block Dirt { get; }
        public Block Grass { get; }
        public Block Water { get; }

        public bool Equals(TerrainSurfacePalette other) =>
            Stone == other.Stone && Dirt == other.Dirt &&
            Grass == other.Grass && Water == other.Water;

        public override bool Equals(object obj) =>
            obj is TerrainSurfacePalette other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Stone.GetHashCode();
                hash = hash * 397 ^ Dirt.GetHashCode();
                hash = hash * 397 ^ Grass.GetHashCode();
                return hash * 397 ^ Water.GetHashCode();
            }
        }
    }

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
            long maximumBlockBudget = DefaultMaximumBlockBudget,
            TerrainGenerationProfile profile = TerrainGenerationProfile.Normal)
            : this(
                originX,
                originZ,
                width,
                depth,
                minimumY,
                maximumY,
                baseHeight,
                amplitude,
                frequency,
                octaves,
                seed,
                new TerrainSurfacePalette(solidBlock, solidBlock, solidBlock, default),
                dirtDepth: 0,
                waterLevel: minimumY,
                waterEnabled: false,
                maximumBlockBudget,
                profile,
                validateDistinctPalette: false)
        {
        }

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
            TerrainSurfacePalette palette,
            int dirtDepth,
            int waterLevel,
            long maximumBlockBudget = DefaultMaximumBlockBudget,
            TerrainGenerationProfile profile = TerrainGenerationProfile.Normal)
            : this(
                originX,
                originZ,
                width,
                depth,
                minimumY,
                maximumY,
                baseHeight,
                amplitude,
                frequency,
                octaves,
                seed,
                palette,
                dirtDepth,
                waterLevel,
                waterEnabled: true,
                maximumBlockBudget,
                profile,
                validateDistinctPalette: true)
        {
        }

        private TerrainConfig(
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
            TerrainSurfacePalette palette,
            int dirtDepth,
            int waterLevel,
            bool waterEnabled,
            long maximumBlockBudget,
            TerrainGenerationProfile profile,
            bool validateDistinctPalette)
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
            if (palette.Stone.IsAir || palette.Dirt.IsAir || palette.Grass.IsAir)
                throw new ArgumentException("Generated terrain requires non-air solid palette blocks.", nameof(palette));
            if (waterEnabled && palette.Water.IsAir)
                throw new ArgumentException("Enabled terrain water requires a non-air block.", nameof(palette));
            if (dirtDepth < 0 || dirtDepth > 16)
                throw new ArgumentOutOfRangeException(nameof(dirtDepth));
            if (waterEnabled && (waterLevel < minimumY || waterLevel > maximumY))
                throw new ArgumentOutOfRangeException(nameof(waterLevel));
            if (validateDistinctPalette &&
                (palette.Stone.Id == palette.Dirt.Id ||
                 palette.Stone.Id == palette.Grass.Id ||
                 palette.Stone.Id == palette.Water.Id ||
                 palette.Dirt.Id == palette.Grass.Id ||
                 palette.Dirt.Id == palette.Water.Id ||
                 palette.Grass.Id == palette.Water.Id))
            {
                throw new ArgumentException(
                    "Layered terrain palette block IDs must be distinct.",
                    nameof(palette));
            }
            if (maximumBlockBudget <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBlockBudget));
            if (profile < TerrainGenerationProfile.Normal ||
                profile > TerrainGenerationProfile.Amplified)
                throw new ArgumentOutOfRangeException(nameof(profile));

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
            Palette = palette;
            DirtDepth = dirtDepth;
            WaterLevel = waterLevel;
            WaterEnabled = waterEnabled;
            MaximumBlockBudget = maximumBlockBudget;
            Profile = profile;
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
        public TerrainSurfacePalette Palette { get; }
        public Block StoneBlock => Palette.Stone;
        public Block DirtBlock => Palette.Dirt;
        public Block GrassBlock => Palette.Grass;
        public Block WaterBlock => Palette.Water;
        public Block SolidBlock => StoneBlock;
        public int DirtDepth { get; }
        public int WaterLevel { get; }
        public bool WaterEnabled { get; }
        public long MaximumBlockBudget { get; }
        public TerrainGenerationProfile Profile { get; }
        public int MaximumX => checked(OriginX + Width - 1);
        public int MaximumZ => checked(OriginZ + Depth - 1);

        public bool ContainsColumn(int x, int z) =>
            x >= OriginX && x <= MaximumX && z >= OriginZ && z <= MaximumZ;

        public static TerrainConfig CreateDefault(
            TerrainGenerationProfile profile = TerrainGenerationProfile.Normal) => new TerrainConfig(
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
            palette: new TerrainSurfacePalette(
                new Block(StableBlockId.Compute("minecraft:stone")),
                new Block(StableBlockId.Compute("minecraft:dirt")),
                new Block(StableBlockId.Compute("minecraft:grass_block")),
                new Block(BlockTraitsCatalog.WaterStableId)),
            dirtDepth: 3,
            waterLevel: 7,
            profile: profile);

        public bool Equals(TerrainConfig other) =>
            OriginX == other.OriginX && OriginZ == other.OriginZ &&
            Width == other.Width && Depth == other.Depth &&
            MinimumY == other.MinimumY && MaximumY == other.MaximumY &&
            BaseHeight == other.BaseHeight && Amplitude.Equals(other.Amplitude) &&
            Frequency.Equals(other.Frequency) && Octaves == other.Octaves &&
            Seed == other.Seed && Palette.Equals(other.Palette) &&
            DirtDepth == other.DirtDepth && WaterLevel == other.WaterLevel &&
            WaterEnabled == other.WaterEnabled &&
            Profile == other.Profile &&
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
                hash = hash * 397 ^ Palette.GetHashCode();
                hash = hash * 397 ^ DirtDepth;
                hash = hash * 397 ^ WaterLevel;
                hash = hash * 397 ^ WaterEnabled.GetHashCode();
                hash = hash * 397 ^ (byte)Profile;
                return hash * 397 ^ MaximumBlockBudget.GetHashCode();
            }
        }
    }
}
