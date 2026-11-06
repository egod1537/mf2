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

        public void Forget(int3 chunkCoordinate)
        {
            acceptedBuilds.Remove(chunkCoordinate);
        }

        public void ClearHistory()
        {
            acceptedBuilds.Clear();
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
