using System;
using MF2.World.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Owns one scheduled chunk rebuild and its native allocations. Completion is accepted only
    /// while both the pool generation and chunk version still match the captured snapshot.
    /// </summary>
    public sealed class ChunkMeshBuildRequest : IDisposable
    {
        private ChunkMeshingSnapshot snapshot;
        private ChunkMeshBuildBuffers buffers;
        private JobHandle jobHandle;
        private bool completed;
        private bool disposed;

        internal ChunkMeshBuildRequest(
            ChunkMeshingSnapshot snapshot,
            ChunkMeshBuildBuffers buffers,
            JobHandle jobHandle)
        {
            this.snapshot = snapshot;
            this.buffers = buffers;
            this.jobHandle = jobHandle;
        }

        public ChunkHandle Handle => snapshot.Handle;
        public int3 ChunkCoordinate => snapshot.ChunkCoordinate;
        public ulong Version => snapshot.Version;

        public int FaceCount
        {
            get
            {
                Complete();
                return buffers.FaceCount;
            }
        }

        public NativeArray<ChunkMeshVertex> Vertices
        {
            get
            {
                Complete();
                return buffers.Vertices.AsArray();
            }
        }

        public NativeArray<uint> OpaqueIndices
        {
            get
            {
                Complete();
                return buffers.OpaqueIndices.AsArray();
            }
        }

        public NativeArray<uint> CutoutIndices
        {
            get
            {
                Complete();
                return buffers.CutoutIndices.AsArray();
            }
        }

        public NativeArray<uint> TransparentIndices
        {
            get
            {
                Complete();
                return buffers.TransparentIndices.AsArray();
            }
        }

        public void Complete()
        {
            ThrowIfDisposed();
            if (completed)
                return;
            jobHandle.Complete();
            completed = true;
        }

        public bool TryApply(WorldCore world, Mesh mesh)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            Complete();
            if (!snapshot.IsCurrent(world))
                return false;

            ChunkMeshApplier.Apply(
                mesh,
                buffers.Vertices.AsArray(),
                buffers.OpaqueIndices.AsArray(),
                buffers.CutoutIndices.AsArray(),
                buffers.TransparentIndices.AsArray());
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            jobHandle.Complete();
            buffers.Dispose();
            snapshot.Dispose();
            completed = true;
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkMeshBuildRequest));
        }
    }
}
