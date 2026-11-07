using UnityEngine;

namespace MF2.World.Rendering
{
    [CreateAssetMenu(fileName = "ChunkMaterialSet", menuName = "MF2/World/Chunk Material Set")]
    public sealed class ChunkMaterialSet : ScriptableObject
    {
        [SerializeField] private Texture2D atlas;
        [SerializeField] private Material opaque;
        [SerializeField] private Material cutout;
        [SerializeField] private Material transparent;
        [SerializeField] private Material water;

        public Texture2D Atlas => atlas;
        public Material Opaque => opaque;
        public Material Cutout => cutout;
        public Material Transparent => transparent;
        public Material Water => water;

        public Material[] CreateSharedMaterialArray()
        {
            return new[] { opaque, cutout, transparent, water };
        }

#if UNITY_EDITOR
        internal void Configure(
            Texture2D sourceAtlas,
            Material opaqueMaterial,
            Material cutoutMaterial,
            Material transparentMaterial,
            Material waterMaterial)
        {
            atlas = sourceAtlas;
            opaque = opaqueMaterial;
            cutout = cutoutMaterial;
            transparent = transparentMaterial;
            water = waterMaterial;
        }
#endif
    }
}
