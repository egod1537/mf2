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
            signature = Hash(signature, config.StoneBlock.Id);
            signature = Hash(signature, config.StoneBlock.State);
            signature = Hash(signature, config.DirtBlock.Id);
            signature = Hash(signature, config.DirtBlock.State);
            signature = Hash(signature, config.GrassBlock.Id);
            signature = Hash(signature, config.GrassBlock.State);
            signature = Hash(signature, config.WaterBlock.Id);
            signature = Hash(signature, config.WaterBlock.State);
            signature = Hash(signature, config.DirtDepth);
            signature = Hash(signature, config.WaterLevel);
            signature = Hash(signature, config.WaterEnabled ? 1 : 0);

            for (var zOffset = 0; zOffset < config.Depth; zOffset++)
            {
                var z = checked(config.OriginZ + zOffset);
                for (var xOffset = 0; xOffset < config.Width; xOffset++)
                {
                    var x = checked(config.OriginX + xOffset);
                    var height = SampleHeight(x, z);
                    if (!config.WaterEnabled)
                    {
                        EnqueueSpan(world, x, z, config.MinimumY, height, config.SolidBlock);
                        generatedBlocks += (long)height - config.MinimumY + 1L;
                        signature = HashSpan(
                            signature,
                            config.MinimumY,
                            height,
                            config.SolidBlock);
                    }
                    else
                    {
                        var dirtStart = Math.Max(config.MinimumY, height - config.DirtDepth);
                        var stoneEnd = dirtStart - 1;
                        if (stoneEnd >= config.MinimumY)
                        {
                            EnqueueSpan(
                                world,
                                x,
                                z,
                                config.MinimumY,
                                stoneEnd,
                                config.StoneBlock);
                            generatedBlocks += (long)stoneEnd - config.MinimumY + 1L;
                            signature = HashSpan(
                                signature,
                                config.MinimumY,
                                stoneEnd,
                                config.StoneBlock);
                        }

                        if (height < config.WaterLevel)
                        {
                            EnqueueSpan(world, x, z, dirtStart, height, config.DirtBlock);
                            generatedBlocks += (long)height - dirtStart + 1L;
                            signature = HashSpan(
                                signature,
                                dirtStart,
                                height,
                                config.DirtBlock);

                            var waterStart = height + 1;
                            EnqueueSpan(
                                world,
                                x,
                                z,
                                waterStart,
                                config.WaterLevel,
                                config.WaterBlock);
                            generatedBlocks += (long)config.WaterLevel - waterStart + 1L;
                            signature = HashSpan(
                                signature,
                                waterStart,
                                config.WaterLevel,
                                config.WaterBlock);
                        }
                        else
                        {
                            if (dirtStart <= height - 1)
                            {
                                EnqueueSpan(
                                    world,
                                    x,
                                    z,
                                    dirtStart,
                                    height - 1,
                                    config.DirtBlock);
                                generatedBlocks += (long)height - dirtStart;
                                signature = HashSpan(
                                    signature,
                                    dirtStart,
                                    height - 1,
                                    config.DirtBlock);
                            }

                            EnqueueSpan(world, x, z, height, height, config.GrassBlock);
                            generatedBlocks++;
                            signature = HashSpan(signature, height, height, config.GrassBlock);
                        }
                    }
                    queuedColumns++;
                    signature = Hash(signature, x);
                    signature = Hash(signature, z);
                    signature = Hash(signature, height);

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

        private static void EnqueueSpan(
            SimulationWorld world,
            int x,
            int z,
            int minimumY,
            int maximumY,
            Block block)
        {
            if (minimumY > maximumY)
                return;

            world.Enqueue(WorldCommand.FillRegion(
                new int3(x, minimumY, z),
                new int3(x, maximumY, z),
                block));
        }

        private static ulong HashSpan(ulong hash, int minimumY, int maximumY, Block block)
        {
            hash = Hash(hash, minimumY);
            hash = Hash(hash, maximumY);
            hash = Hash(hash, block.Id);
            return Hash(hash, block.State);
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
