using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Rendering
{
    /// <summary>Maintains one pooled renderer GameObject per active chunk.</summary>
    public sealed class ChunkRendererPool : IDisposable
    {
        private const string DitheringProperty = "PROPIXELIZER_DITHERING";
        private const string DitheringKeyword = "PROPIXELIZER_DITHERING_ON";
        private const float OcclusionAlpha = 0.35f;
        private const float OcclusionClipThreshold = 0.05f;
        private static readonly int3[] NoDitheredBlocks = Array.Empty<int3>();

        private readonly Transform parent;
        private readonly Material[] sharedMaterials;
        private readonly Material occlusionDitherMaterial;
        private readonly HideFlags createdHideFlags;
        private readonly bool renderingEnabled;
        private readonly Dictionary<int3, ChunkRenderer> active =
            new Dictionary<int3, ChunkRenderer>();
        private readonly Dictionary<int3, HashSet<int3>> ditheredBlocks =
            new Dictionary<int3, HashSet<int3>>();
        private readonly Stack<ChunkRenderer> inactive = new Stack<ChunkRenderer>();
        private readonly List<ChunkRenderer> created = new List<ChunkRenderer>();
        private bool disposed;

        public ChunkRendererPool(
            Transform parent,
            ChunkMaterialSet materialSet,
            HideFlags createdHideFlags = HideFlags.None,
            bool renderingEnabled = true)
        {
            this.parent = parent;
            this.createdHideFlags = createdHideFlags;
            this.renderingEnabled = renderingEnabled;
            var baseMaterials = materialSet != null
                ? materialSet.CreateSharedMaterialArray()
                : new Material[4];
            occlusionDitherMaterial = renderingEnabled
                ? CreateOcclusionDitherMaterial(baseMaterials[0])
                : null;
            if (occlusionDitherMaterial == null)
            {
                sharedMaterials = baseMaterials;
            }
            else
            {
                sharedMaterials = new Material[ChunkRenderer.OcclusionSubmeshIndex + 1];
                Array.Copy(baseMaterials, sharedMaterials, baseMaterials.Length);
                sharedMaterials[ChunkRenderer.OcclusionSubmeshIndex] = occlusionDitherMaterial;
            }
        }

        public int ActiveCount => active.Count;
        public int CreatedCount => created.Count;

        public ChunkRenderer GetOrCreate(int3 chunkCoordinate)
        {
            ThrowIfDisposed();
            if (active.TryGetValue(chunkCoordinate, out var existing))
                return existing;

            var renderer = inactive.Count > 0 ? inactive.Pop() : CreateRenderer();
            renderer.gameObject.SetActive(true);
            renderer.Configure(chunkCoordinate, sharedMaterials);
            renderer.SetDitheredBlocks(
                ditheredBlocks.TryGetValue(chunkCoordinate, out var localBlocks)
                    ? localBlocks
                    : NoDitheredBlocks);
            active.Add(chunkCoordinate, renderer);
            return renderer;
        }

        public bool TryGet(int3 chunkCoordinate, out ChunkRenderer renderer)
        {
            ThrowIfDisposed();
            return active.TryGetValue(chunkCoordinate, out renderer);
        }

        public void SetDitheredBlocks(IReadOnlyCollection<int3> worldPositions)
        {
            ThrowIfDisposed();
            if (worldPositions == null)
                throw new ArgumentNullException(nameof(worldPositions));

            ditheredBlocks.Clear();
            foreach (var worldPosition in worldPositions)
            {
                ChunkCoordinates.Split(worldPosition, out var chunk, out var local);
                if (!ditheredBlocks.TryGetValue(chunk, out var localBlocks))
                {
                    localBlocks = new HashSet<int3>();
                    ditheredBlocks.Add(chunk, localBlocks);
                }
                localBlocks.Add(local);
            }

            foreach (var pair in active)
            {
                pair.Value.SetDitheredBlocks(
                    ditheredBlocks.TryGetValue(pair.Key, out var localBlocks)
                        ? localBlocks
                        : NoDitheredBlocks);
            }
        }

        public bool Release(int3 chunkCoordinate)
        {
            ThrowIfDisposed();
            if (!active.TryGetValue(chunkCoordinate, out var renderer))
                return false;

            active.Remove(chunkCoordinate);
            renderer.Clear();
            renderer.gameObject.SetActive(false);
            inactive.Push(renderer);
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            active.Clear();
            ditheredBlocks.Clear();
            inactive.Clear();
            for (var i = 0; i < created.Count; i++)
            {
                var renderer = created[i];
                if (renderer == null)
                    continue;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(renderer.gameObject);
                else
                    UnityEngine.Object.DestroyImmediate(renderer.gameObject);
            }

            created.Clear();
            DestroyOwnedMaterial(occlusionDitherMaterial);
            disposed = true;
        }

        private ChunkRenderer CreateRenderer()
        {
            var gameObject = new GameObject("Chunk Renderer")
            {
                hideFlags = createdHideFlags
            };
            if (parent != null)
                gameObject.transform.SetParent(parent, false);
            var renderer = gameObject.AddComponent<ChunkRenderer>();
            renderer.SetRenderingEnabled(renderingEnabled);
            created.Add(renderer);
            return renderer;
        }

        private static Material CreateOcclusionDitherMaterial(Material source)
        {
            if (source == null || !source.HasProperty(DitheringProperty))
                return null;

            var material = new Material(source)
            {
                name = $"{source.name} (Player Occlusion Dither)",
                hideFlags = HideFlags.DontSave
            };
            material.SetFloat(DitheringProperty, 1f);
            material.EnableKeyword(DitheringKeyword);
            SetAlpha(material, "_BaseColor", OcclusionAlpha);
            SetAlpha(material, "_Color", OcclusionAlpha);
            if (material.HasProperty("AlphaClipThreshold"))
                material.SetFloat("AlphaClipThreshold", OcclusionClipThreshold);
            if (material.HasProperty("_AlphaClipThreshold"))
                material.SetFloat("_AlphaClipThreshold", OcclusionClipThreshold);
            return material;
        }

        private static void SetAlpha(Material material, string property, float alpha)
        {
            if (!material.HasProperty(property))
                return;
            var color = material.GetColor(property);
            color.a = alpha;
            material.SetColor(property, color);
        }

        private static void DestroyOwnedMaterial(Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(material);
            else
                UnityEngine.Object.DestroyImmediate(material);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkRendererPool));
        }
    }
}
