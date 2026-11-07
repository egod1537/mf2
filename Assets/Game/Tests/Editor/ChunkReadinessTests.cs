using MF2.World.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.Tests
{
    public sealed class ChunkReadinessTests
    {
        [Test]
        public void DataAlone_DoesNotReleaseCollisionBarrier()
        {
            var readiness = new ChunkReadiness();
            readiness.MarkDataReady(new int3(1, 0, 2), 4);

            Assert.That(readiness.HasData, Is.True);
            Assert.That(readiness.IsCollisionReady, Is.False);
            Assert.That(readiness.IsActive, Is.False);
        }

        [Test]
        public void MatchingCollisionVersion_ReleasesCollisionBeforeVisual()
        {
            var readiness = new ChunkReadiness();
            readiness.MarkDataReady(new int3(1, 0, 2), 4);

            Assert.That(readiness.TryMarkCollisionReady(4), Is.True);
            Assert.That(readiness.IsCollisionReady, Is.True);
            Assert.That(readiness.IsVisualReady, Is.False);
        }

        [Test]
        public void StaleBuildResult_IsRejectedAfterInvalidation()
        {
            var readiness = new ChunkReadiness();
            readiness.MarkDataReady(int3.zero, 4);
            readiness.MarkDataReady(int3.zero, 5);

            Assert.That(readiness.TryMarkCollisionReady(4), Is.False);
            Assert.That(readiness.TryMarkVisualReady(4), Is.False);
            Assert.That(readiness.TryMarkCollisionReady(5), Is.True);
        }

        [Test]
        public void ChunkRenderer_AlwaysCarriesOneChunkLevelMeshCollider()
        {
            var gameObject = new GameObject("Chunk Renderer Collider Test");
            try
            {
                gameObject.AddComponent<ChunkRenderer>();
                Assert.That(gameObject.GetComponents<MeshCollider>(), Has.Length.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CollisionOnlyPool_DisablesVisualRendererButKeepsColliderEnabled()
        {
            var root = new GameObject("Collision Only Pool Test");
            try
            {
                using var pool = new ChunkRendererPool(
                    root.transform,
                    materialSet: null,
                    renderingEnabled: false);
                var renderer = pool.GetOrCreate(int3.zero);

                Assert.That(renderer.RenderingEnabled, Is.False);
                Assert.That(renderer.GetComponent<MeshCollider>().enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
