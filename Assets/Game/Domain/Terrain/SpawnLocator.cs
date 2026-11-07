using System;
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
                    if (world.GetBlockOrAir(new int3(x, y, z)).IsAir ||
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
