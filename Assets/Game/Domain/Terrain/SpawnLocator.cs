using System;
using System.Collections.Generic;
using MF2.World.Registry;
using MF2.World.Storage;
using Unity.Mathematics;

namespace MF2.Game.Domain
{
    /// <summary>Finds a repeatable feet position above authoritative solid block data.</summary>
    public sealed class SpawnLocator
    {
        public bool TryLocate(
            IReadOnlySimulationWorld world,
            TerrainConfig config,
            int playerOrdinal,
            out float3 position,
            int requiredClearanceBlocks = 2,
            float groundMargin = 0.05f)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (playerOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(playerOrdinal));
            if (requiredClearanceBlocks <= 0)
                throw new ArgumentOutOfRangeException(nameof(requiredClearanceBlocks));
            if (groundMargin < 0f || float.IsNaN(groundMargin) || float.IsInfinity(groundMargin))
                throw new ArgumentOutOfRangeException(nameof(groundMargin));

            var insetX = config.Width > 2 ? 1 : 0;
            var insetZ = config.Depth > 2 ? 1 : 0;
            var usableWidth = config.Width - insetX * 2;
            var usableDepth = config.Depth - insetZ * 2;
            var candidateCount = checked(usableWidth * usableDepth);
            var start = candidateCount == 0
                ? 0
                : (int)((long)playerOrdinal * 97L % candidateCount);

            for (var candidateOffset = 0; candidateOffset < candidateCount; candidateOffset++)
            {
                var candidate = (start + candidateOffset) % candidateCount;
                var x = config.OriginX + insetX + candidate % usableWidth;
                var z = config.OriginZ + insetZ + candidate / usableWidth;
                for (var y = config.MaximumY; y >= config.MinimumY; y--)
                {
                    if (!BlockTraitsCatalog.IsSolid(world.GetBlockOrAir(new int3(x, y, z))) ||
                        !HasClearance(world, x, y, z, requiredClearanceBlocks))
                    {
                        continue;
                    }

                    position = new float3(x + 0.5f, y + 1f + groundMargin, z + 0.5f);
                    return true;
                }
            }

            position = default;
            return false;
        }

        /// <summary>
        /// Restored sessions intentionally do not regenerate terrain and therefore have no
        /// TerrainConfig. Derive deterministic candidate columns from the validated sparse world
        /// snapshot and still apply the same solid-floor/dry-clearance policy.
        /// </summary>
        public bool TryLocate(
            IReadOnlySimulationWorld world,
            WorldStorageSnapshot snapshot,
            int playerOrdinal,
            out float3 position,
            int requiredClearanceBlocks = 2,
            float groundMargin = 0.05f)
        {
            ValidateArguments(world, playerOrdinal, requiredClearanceBlocks, groundMargin);
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var highestSolidByColumn = new Dictionary<int2, int>();
            for (var chunkIndex = 0; chunkIndex < snapshot.Chunks.Count; chunkIndex++)
            {
                var chunk = snapshot.Chunks[chunkIndex];
                for (var blockIndex = 0; blockIndex < chunk.Blocks.Count; blockIndex++)
                {
                    var stored = chunk.Blocks[blockIndex];
                    var block = stored.ToBlock();
                    if (!BlockTraitsCatalog.IsSolid(block))
                        continue;

                    var worldPosition =
                        chunk.ChunkCoordinate * MF2.World.Core.ChunkCoordinates.Size +
                        MF2.World.Core.ChunkCoordinates.FromIndex(stored.Index);
                    var column = new int2(worldPosition.x, worldPosition.z);
                    if (!highestSolidByColumn.TryGetValue(column, out var current) ||
                        worldPosition.y > current)
                    {
                        highestSolidByColumn[column] = worldPosition.y;
                    }
                }
            }

            var columns = new List<KeyValuePair<int2, int>>(highestSolidByColumn);
            columns.Sort((left, right) =>
            {
                var z = left.Key.y.CompareTo(right.Key.y);
                return z != 0 ? z : left.Key.x.CompareTo(right.Key.x);
            });
            if (columns.Count == 0)
            {
                position = default;
                return false;
            }

            var start = (int)((long)playerOrdinal * 97L % columns.Count);
            for (var offset = 0; offset < columns.Count; offset++)
            {
                var candidate = columns[(start + offset) % columns.Count];
                var x = candidate.Key.x;
                var z = candidate.Key.y;
                var y = candidate.Value;
                if (!HasClearance(world, x, y, z, requiredClearanceBlocks))
                    continue;

                position = new float3(x + 0.5f, y + 1f + groundMargin, z + 0.5f);
                return true;
            }

            position = default;
            return false;
        }

        private static void ValidateArguments(
            IReadOnlySimulationWorld world,
            int playerOrdinal,
            int requiredClearanceBlocks,
            float groundMargin)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (playerOrdinal < 0)
                throw new ArgumentOutOfRangeException(nameof(playerOrdinal));
            if (requiredClearanceBlocks <= 0)
                throw new ArgumentOutOfRangeException(nameof(requiredClearanceBlocks));
            if (groundMargin < 0f || float.IsNaN(groundMargin) || float.IsInfinity(groundMargin))
                throw new ArgumentOutOfRangeException(nameof(groundMargin));
        }

        private static bool HasClearance(
            IReadOnlySimulationWorld world,
            int x,
            int surfaceY,
            int z,
            int requiredClearanceBlocks)
        {
            for (var offset = 1; offset <= requiredClearanceBlocks; offset++)
            {
                if (!world.GetBlockOrAir(new int3(x, surfaceY + offset, z)).IsAir)
                    return false;
            }
            return true;
        }
    }
}
