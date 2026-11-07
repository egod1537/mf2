using MF2.World.Core;

namespace MF2.World.Registry
{
    /// <summary>
    /// Gameplay traits are independent from render pass. The zero value intentionally preserves
    /// the legacy behavior: every registered non-air block is solid and opaque unless explicitly
    /// listed here.
    /// </summary>
    public enum BlockTraitsKind : byte
    {
        SolidOpaque = 0,
        Liquid = 1,
        NonSolidNonOccluding = 2
    }

    public static class BlockTraitsCatalog
    {
        public const string WaterCanonicalName = "minecraft:water";

        public static uint WaterStableId => StableBlockId.Compute(WaterCanonicalName);

        public static BlockTraitsKind GetKind(uint stableId) =>
            stableId == WaterStableId
                ? BlockTraitsKind.Liquid
                : BlockTraitsKind.SolidOpaque;

        public static bool IsSolid(Block block) =>
            !block.IsAir && GetKind(block.Id) == BlockTraitsKind.SolidOpaque;

        public static bool IsLiquid(Block block) =>
            !block.IsAir && GetKind(block.Id) == BlockTraitsKind.Liquid;
    }
}
