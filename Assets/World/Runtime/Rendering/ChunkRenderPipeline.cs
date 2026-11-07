using System;
using System.Collections.Generic;
using MF2.World.Core;
using Unity.Collections;
using Unity.Mathematics;

namespace MF2.World.Rendering
{
    /// <summary>
    /// Schedules the latest Flush dirty set, then applies completed MeshData on the main thread.
    /// Repeating the call for the same version is a no-op.
    /// </summary>
    public sealed class ChunkRenderPipeline
    {
        private readonly Dictionary<int3, AcceptedBuild> acceptedBuilds =
            new Dictionary<int3, AcceptedBuild>();
        private readonly List<ChunkMeshBuildRequest> requests =
            new List<ChunkMeshBuildRequest>();
        private readonly HashSet<ChunkHandle> scheduled = new HashSet<ChunkHandle>();

        public int RebuildDirtyNow(
            WorldCore world,
            BlockRenderTable renderTable,
            ChunkRendererPool rendererPool,
            Allocator allocator = Allocator.TempJob)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (renderTable == null)
                throw new ArgumentNullException(nameof(renderTable));
            if (rendererPool == null)
                throw new ArgumentNullException(nameof(rendererPool));

            requests.Clear();
            scheduled.Clear();
            try
            {
                var dirtyChunks = world.DirtyChunks;
                for (var i = 0; i < dirtyChunks.Length; i++)
                {
                    var dirty = dirtyChunks[i];
                    if (acceptedBuilds.TryGetValue(dirty.ChunkCoordinate, out var accepted) &&
                        accepted.Handle == dirty.Handle &&
                        accepted.Version == dirty.Version)
                    {
                        continue;
                    }
                    if (!scheduled.Add(dirty.Handle))
                        continue;
                    if (ChunkMeshBuilder.TrySchedule(
                            world,
                            dirty.Handle,
                            renderTable,
                            out var request,
                            allocator))
                    {
                        requests.Add(request);
                    }
                }

                var appliedCount = 0;
                for (var i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    if (!world.IsChunkVersionCurrent(request.Handle, request.Version))
                        continue;
                    var renderer = rendererPool.GetOrCreate(request.ChunkCoordinate);
                    if (!renderer.Apply(world, request))
                        continue;

                    acceptedBuilds[request.ChunkCoordinate] =
                        new AcceptedBuild(request.Handle, request.Version);
                    appliedCount++;
                }

                return appliedCount;
            }
            finally
            {
                for (var i = 0; i < requests.Count; i++)
                    requests[i].Dispose();
                requests.Clear();
                scheduled.Clear();
            }
        }

        /// <summary>Builds every resident chunk when a new scene-side view attaches.</summary>
        public int RebuildAllNow(
            WorldCore world,
            BlockRenderTable renderTable,
            ChunkRendererPool rendererPool,
            Allocator allocator = Allocator.TempJob)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (renderTable == null)
                throw new ArgumentNullException(nameof(renderTable));
            if (rendererPool == null)
                throw new ArgumentNullException(nameof(rendererPool));

            requests.Clear();
            scheduled.Clear();
            var handles = world.GetLoadedChunkHandles(Allocator.Temp);
            try
            {
                for (var i = 0; i < handles.Length; i++)
                {
                    var handle = handles[i];
                    if (!world.TryGetChunkCoordinate(handle, out var coordinate) ||
                        !world.TryGetChunkVersion(handle, out var version))
                    {
                        continue;
                    }
                    if (acceptedBuilds.TryGetValue(coordinate, out var accepted) &&
                        accepted.Handle == handle &&
                        accepted.Version == version)
                    {
                        continue;
                    }
                    if (!scheduled.Add(handle))
                        continue;
                    if (ChunkMeshBuilder.TrySchedule(
                            world,
                            handle,
                            renderTable,
                            out var request,
                            allocator))
                    {
                        requests.Add(request);
                    }
                }

                var appliedCount = 0;
                for (var i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    if (!world.IsChunkVersionCurrent(request.Handle, request.Version))
                        continue;
                    var renderer = rendererPool.GetOrCreate(request.ChunkCoordinate);
                    if (!renderer.Apply(world, request))
                        continue;

                    acceptedBuilds[request.ChunkCoordinate] =
                        new AcceptedBuild(request.Handle, request.Version);
                    appliedCount++;
                }

                return appliedCount;
            }
            finally
            {
                handles.Dispose();
                for (var i = 0; i < requests.Count; i++)
                    requests[i].Dispose();
                requests.Clear();
                scheduled.Clear();
            }
        }

        public void Forget(int3 chunkCoordinate)
        {
            acceptedBuilds.Remove(chunkCoordinate);
        }

        public void ClearHistory()
        {
            acceptedBuilds.Clear();
        }

        /// <summary>
        /// Returns true only when every currently loaded chunk has a collider projection for its
        /// current handle generation and data version. An empty world is collision-ready.
        /// </summary>
        public bool AreAllLoadedChunkCollidersReady(
            WorldCore world,
            ChunkRendererPool rendererPool)
        {
            if (world == null || rendererPool == null)
                return false;

            var handles = world.GetLoadedChunkHandles(Allocator.Temp);
            try
            {
                for (var i = 0; i < handles.Length; i++)
                {
                    var handle = handles[i];
                    if (!world.TryGetChunkCoordinate(handle, out var coordinate) ||
                        !world.TryGetChunkVersion(handle, out var version) ||
                        !acceptedBuilds.TryGetValue(coordinate, out var accepted) ||
                        accepted.Handle != handle ||
                        accepted.Version != version ||
                        !rendererPool.TryGet(coordinate, out var renderer) ||
                        !renderer.IsCollisionReadyFor(handle, version))
                    {
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                handles.Dispose();
            }
        }

        private readonly struct AcceptedBuild
        {
            public readonly ChunkHandle Handle;
            public readonly ulong Version;

            public AcceptedBuild(ChunkHandle handle, ulong version)
            {
                Handle = handle;
                Version = version;
            }
        }
    }
}
