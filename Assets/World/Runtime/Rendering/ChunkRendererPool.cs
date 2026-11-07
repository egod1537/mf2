using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Rendering
{
    /// <summary>Maintains one pooled renderer GameObject per active chunk.</summary>
    public sealed class ChunkRendererPool : IDisposable
    {
        private readonly Transform parent;
        private readonly Material[] sharedMaterials;
        private readonly HideFlags createdHideFlags;
        private readonly bool renderingEnabled;
        private readonly Dictionary<int3, ChunkRenderer> active =
            new Dictionary<int3, ChunkRenderer>();
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
            sharedMaterials = materialSet != null
                ? materialSet.CreateSharedMaterialArray()
                : new Material[4];
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
            active.Add(chunkCoordinate, renderer);
            return renderer;
        }

        public bool TryGet(int3 chunkCoordinate, out ChunkRenderer renderer)
        {
            ThrowIfDisposed();
            return active.TryGetValue(chunkCoordinate, out renderer);
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

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkRendererPool));
        }
    }
}
