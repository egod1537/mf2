using System;
using MF2.World.Core;
using Unity.Mathematics;

namespace MF2.Game.Domain
{
    public interface IWorldInitializer
    {
        TerrainGenerationResult Initialize(SimulationWorld world);
    }

    public sealed class TerrainGenerationResult
    {
        internal TerrainGenerationResult(
            TerrainConfig config,
            long generatedBlockCount,
            int flushCount,
            ulong signature)
        {
            Config = config;
            GeneratedBlockCount = generatedBlockCount;
            FlushCount = flushCount;
            Signature = signature;
        }

        public TerrainConfig Config { get; }
        public long GeneratedBlockCount { get; }
        public int FlushCount { get; }
        public ulong Signature { get; }
    }

    /// <summary>
    /// Deterministic finite terrain bootstrap. It writes only through WorldCommand/Flush and does
    /// not own the generated WorldCore.
    /// </summary>
    public sealed class PerlinTerrainGenerator : IWorldInitializer
    {
        public const int DefaultColumnsPerBatch = 256;

        private readonly TerrainConfig config;
        private readonly int columnsPerBatch;

        public PerlinTerrainGenerator(TerrainConfig config, int columnsPerBatch = DefaultColumnsPerBatch)
        {
            if (columnsPerBatch <= 0)
                throw new ArgumentOutOfRangeException(nameof(columnsPerBatch));
            this.config = config;
            this.columnsPerBatch = columnsPerBatch;
        }

        public TerrainConfig Config => config;

        public int SampleHeight(int worldX, int worldZ)
        {
            var seed = unchecked((uint)config.Seed);
            var seedOffset = new float2(
                (seed & 0xffffu) * 0.03125f + 17.17f,
                (seed >> 16) * 0.03125f - 31.73f);
            var point = new float2(worldX, worldZ) + seedOffset;
            var value = 0f;
            var weight = 1f;
            var weightSum = 0f;
            var frequency = config.Frequency;

            for (var octave = 0; octave < config.Octaves; octave++)
            {
                value += noise.snoise(point * frequency) * weight;
                weightSum += weight;
                frequency *= 2f;
                weight *= 0.5f;
            }

            var normalized = weightSum > 0f ? value / weightSum : 0f;
            var height = (int)math.round(config.BaseHeight + normalized * config.Amplitude);
            return math.clamp(height, config.MinimumY, config.MaximumY);
        }

        public TerrainGenerationResult Initialize(SimulationWorld world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (world.LoadedChunkCount != 0)
                throw new InvalidOperationException("Procedural terrain may initialize only an empty world.");

            long generatedBlocks = 0;
            var queuedColumns = 0;
            var flushCount = 0;
            var signature = 14695981039346656037UL;

            for (var zOffset = 0; zOffset < config.Depth; zOffset++)
            {
                var z = checked(config.OriginZ + zOffset);
                for (var xOffset = 0; xOffset < config.Width; xOffset++)
                {
                    var x = checked(config.OriginX + xOffset);
                    var height = SampleHeight(x, z);
                    world.Enqueue(WorldCommand.FillRegion(
                        new int3(x, config.MinimumY, z),
                        new int3(x, height, z),
                        config.SolidBlock));
                    queuedColumns++;
                    generatedBlocks += (long)height - config.MinimumY + 1L;
                    signature = Hash(signature, x);
                    signature = Hash(signature, z);
                    signature = Hash(signature, height);
                    signature = Hash(signature, config.SolidBlock.Id);

                    if (queuedColumns < columnsPerBatch)
                        continue;
                    world.FlushAndCapture();
                    flushCount++;
                    queuedColumns = 0;
                }
            }

            if (queuedColumns > 0)
            {
                world.FlushAndCapture();
                flushCount++;
            }

            return new TerrainGenerationResult(config, generatedBlocks, flushCount, signature);
        }

        private static ulong Hash(ulong hash, int value) => Hash(hash, unchecked((uint)value));

        private static ulong Hash(ulong hash, uint value)
        {
            unchecked
            {
                hash = (hash ^ (byte)value) * 1099511628211UL;
                hash = (hash ^ (byte)(value >> 8)) * 1099511628211UL;
                hash = (hash ^ (byte)(value >> 16)) * 1099511628211UL;
                return (hash ^ (byte)(value >> 24)) * 1099511628211UL;
            }
        }
    }
}
