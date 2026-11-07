using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MF2.World.Core;
using MF2.World.Registry;
using MF2.World.Rendering;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MF2.World.Tests
{
    public sealed class ChunkMeshingTests
    {
        private const string RenderManifestPath =
            "Assets/World/Rendering/BlockRenderManifest.asset";

        [Test]
        public void SingleCube_ProducesExactlySixFaces()
        {
            using (var world = new WorldCore())
            using (var table = CreateTable(1))
            {
                world.Enqueue(WorldCommand.SetBlock(new int3(0, 0, 0), new Block(1, 7)));
                world.Flush();

                Assert.That(world.TryGetChunkHandle(int3.zero, out var handle), Is.True);
                using (var request = Schedule(world, handle, table))
                {
                    Assert.That(request.FaceCount, Is.EqualTo(6));
                    Assert.That(request.Vertices.Length, Is.EqualTo(24));
                    Assert.That(request.OpaqueIndices.Length, Is.EqualTo(36));
                    Assert.That(request.CutoutIndices.Length, Is.Zero);
                    Assert.That(request.TransparentIndices.Length, Is.Zero);
                }
            }
        }

        [Test]
        public void AdjacentCubes_ProduceTenExposedFaces()
        {
            using (var world = new WorldCore())
            using (var table = CreateTable(1))
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                world.Enqueue(WorldCommand.SetBlock(new int3(1, 0, 0), new Block(1, 0)));
                world.Flush();

                world.TryGetChunkHandle(int3.zero, out var handle);
                using (var request = Schedule(world, handle, table))
                    Assert.That(request.FaceCount, Is.EqualTo(10));
            }
        }

        [Test]
        public void TouchingBoundaryCubes_HideSeamAndBothChunksAreDirtyOnEdit()
        {
            using (var world = new WorldCore())
            using (var table = CreateTable(1))
            {
                world.Enqueue(WorldCommand.SetBlock(new int3(15, 0, 0), new Block(1, 0)));
                world.Enqueue(WorldCommand.SetBlock(new int3(16, 0, 0), new Block(1, 0)));
                world.Flush();

                Assert.That(world.DirtyChunks.Length, Is.EqualTo(2));
                world.TryGetChunkHandle(int3.zero, out var leftHandle);
                world.TryGetChunkHandle(new int3(1, 0, 0), out var rightHandle);
                using (var left = Schedule(world, leftHandle, table))
                using (var right = Schedule(world, rightHandle, table))
                {
                    Assert.That(left.FaceCount, Is.EqualTo(5));
                    Assert.That(right.FaceCount, Is.EqualTo(5));
                }

                world.Enqueue(WorldCommand.RemoveBlock(new int3(15, 0, 0)));
                world.Flush();
                Assert.That(world.DirtyChunks.Length, Is.EqualTo(2));
                Assert.That(
                    world.DirtyChunks.Select(item => item.ChunkCoordinate).ToArray(),
                    Is.EquivalentTo(new[] { int3.zero, new int3(1, 0, 0) }));
            }
        }

        [Test]
        public void ExportedManifest_PreservesMixedFaceUvsAndHalfTexelEdges()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(RenderManifestPath);
            var registry = AssetDatabase.LoadAssetAtPath<BlockRegistryManifest>(
                "Assets/World/Registry/BlockRegistryManifest.asset");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(registry, Is.Not.Null);
            Assert.That(manifest.Entries.Count, Is.EqualTo(401));
            Assert.That(manifest.SourceAtlasGuid, Is.EqualTo("246bc2e1010431c46b3649b3d5a6936e"));

            var stone = FindEntry(manifest, "minecraft:stone");
            var dirt = FindEntry(manifest, "minecraft:dirt");
            var oak = FindEntry(manifest, "minecraft:oak_log");
            var grass = FindEntry(manifest, "minecraft:grass_block");
            AssertEntryUvsMatchPrefab(registry, stone);
            AssertEntryUvsMatchPrefab(registry, dirt);
            AssertEntryUvsMatchPrefab(registry, grass);
            AssertEntryUvsMatchPrefab(registry, oak);
            Assert.That(stone.Faces[(int)BlockFace.North].Uv0,
                Is.Not.EqualTo(dirt.Faces[(int)BlockFace.North].Uv0));
            Assert.That(oak.Faces[(int)BlockFace.North].Uv0,
                Is.Not.EqualTo(oak.Faces[(int)BlockFace.Up].Uv0));
            Assert.That(grass.Faces[(int)BlockFace.North].Uv0,
                Is.Not.EqualTo(grass.Faces[(int)BlockFace.Up].Uv0));

            AssertHalfTexel(oak.Faces[(int)BlockFace.North].Uv0, manifest.Atlas.width);
            AssertHalfTexel(oak.Faces[(int)BlockFace.Up].Uv3, manifest.Atlas.width);

            using (var table = manifest.CreateRuntimeTable(Allocator.TempJob))
            using (var world = new WorldCore())
            {
                var entries = new[] { stone, dirt, grass, oak };
                for (var i = 0; i < entries.Length; i++)
                {
                    world.Enqueue(WorldCommand.SetBlock(
                        new int3(i * 2, 0, 0),
                        new Block(entries[i].StableId, 0)));
                }
                world.Flush();
                world.TryGetChunkHandle(int3.zero, out var handle);

                using (var request = Schedule(world, handle, table))
                {
                    var vertices = request.Vertices;
                    Assert.That(request.FaceCount, Is.EqualTo(24));
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var vertexOffset = i * 24;
                        Assert.That(ToVector2(vertices[vertexOffset].Uv),
                            Is.EqualTo(entries[i].Faces[(int)BlockFace.North].Uv0));
                        Assert.That(ToVector2(vertices[vertexOffset + 16].Uv),
                            Is.EqualTo(entries[i].Faces[(int)BlockFace.Up].Uv0));
                    }
                }
            }
        }

        [Test]
        public void ExportedAtlas_ContainsAndRoutesRealCutoutAndTransparentBlocks()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<BlockRenderManifest>(RenderManifestPath);
            var cutout = manifest.Entries.First(entry =>
                entry.Faces.Any(face => face.RenderPass == BlockRenderPass.Cutout));
            var transparent = manifest.Entries.First(entry =>
                entry.Faces.Any(face => face.RenderPass == BlockRenderPass.Transparent));

            using (var table = manifest.CreateRuntimeTable(Allocator.TempJob))
            using (var world = new WorldCore())
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(cutout.StableId)));
                world.Enqueue(WorldCommand.SetBlock(new int3(2, 0, 0), new Block(transparent.StableId)));
                world.Flush();
                world.TryGetChunkHandle(int3.zero, out var handle);

                using (var request = Schedule(world, handle, table))
                {
                    Assert.That(request.CutoutIndices.Length, Is.GreaterThan(0),
                        $"Expected real cutout indices for {cutout.CanonicalName}.");
                    Assert.That(request.TransparentIndices.Length, Is.GreaterThan(0),
                        $"Expected real transparent indices for {transparent.CanonicalName}.");
                }
            }
        }

        [Test]
        public void MeshDataApply_UsesThreeStablePassSubmeshes()
        {
            var mesh = new Mesh();
            try
            {
                using (var world = new WorldCore())
                using (var table = CreateTable(1))
                {
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    world.TryGetChunkHandle(int3.zero, out var handle);
                    using (var request = Schedule(world, handle, table))
                    {
                        Assert.That(request.TryApply(world, mesh), Is.True);
                        Assert.That(mesh.vertexCount, Is.EqualTo(24));
                        Assert.That(mesh.subMeshCount, Is.EqualTo(3));
                        Assert.That(mesh.GetIndexCount(0), Is.EqualTo(36));
                        Assert.That(mesh.GetIndexCount(1), Is.Zero);
                        Assert.That(mesh.GetIndexCount(2), Is.Zero);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FacePasses_RouteToOpaqueCutoutAndTransparentIndexLists()
        {
            var opaque = CreateFace(BlockRenderPass.Opaque);
            var cutout = CreateFace(BlockRenderPass.Cutout);
            var transparent = CreateFace(BlockRenderPass.Transparent);
            using (var table = new BlockRenderTable(new[]
                   {
                       new BlockRenderDefinition
                       {
                           Id = 1,
                           GeometryKind = BlockGeometryKind.Cube,
                           North = opaque,
                           South = opaque,
                           East = cutout,
                           West = cutout,
                           Up = transparent,
                           Down = transparent
                       }
                   }))
            using (var world = new WorldCore())
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                world.Flush();
                world.TryGetChunkHandle(int3.zero, out var handle);
                using (var request = Schedule(world, handle, table))
                {
                    Assert.That(request.OpaqueIndices.Length, Is.EqualTo(12));
                    Assert.That(request.CutoutIndices.Length, Is.EqualTo(12));
                    Assert.That(request.TransparentIndices.Length, Is.EqualTo(12));
                }
            }
        }

        [Test]
        public void ChangedVersion_RejectsStaleScheduledResult()
        {
            var mesh = new Mesh();
            try
            {
                using (var world = new WorldCore())
                using (var table = CreateTable(1))
                {
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    world.TryGetChunkHandle(int3.zero, out var handle);
                    using (var request = Schedule(world, handle, table))
                    {
                        world.Enqueue(WorldCommand.SetBlock(new int3(1, 0, 0), new Block(1, 0)));
                        world.Flush();
                        Assert.That(request.TryApply(world, mesh), Is.False);
                        Assert.That(mesh.vertexCount, Is.Zero);
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void JsonSaveReload_ProducesIdenticalMesh()
        {
            var records = new[]
            {
                new BlockRecord(new int3(-1, 2, 3), new Block(1, 11)),
                new BlockRecord(new int3(0, 2, 3), new Block(1, 12)),
                new BlockRecord(new int3(4, 5, 6), new Block(1, 13))
            };

            var save = new SerializedBlockSet
            {
                records = records.Select(record => new SerializedBlockRecord
                {
                    x = record.Position.x,
                    y = record.Position.y,
                    z = record.Position.z,
                    id = record.Block.Id,
                    state = record.Block.State
                }).ToArray()
            };
            var tempPath = Path.GetTempFileName();
            SerializedBlockSet loaded;
            try
            {
                File.WriteAllText(tempPath, JsonUtility.ToJson(save));
                loaded = JsonUtility.FromJson<SerializedBlockSet>(File.ReadAllText(tempPath));
            }
            finally
            {
                File.Delete(tempPath);
            }

            var loadedRecords = loaded.records.Select(record => new BlockRecord(
                new int3(record.x, record.y, record.z),
                new Block(record.id, record.state))).ToArray();

            using (var table = CreateTable(1))
            using (var first = BuildWorld(records))
            using (var reloaded = BuildWorld(loadedRecords))
            {
                var coordinate = new int3(-1, 0, 0);
                first.TryGetChunkHandle(coordinate, out var firstHandle);
                reloaded.TryGetChunkHandle(coordinate, out var reloadedHandle);
                using (var firstMesh = Schedule(first, firstHandle, table))
                using (var reloadedMesh = Schedule(reloaded, reloadedHandle, table))
                {
                    AssertNativeArraysEqual(firstMesh.Vertices, reloadedMesh.Vertices);
                    AssertNativeArraysEqual(firstMesh.OpaqueIndices, reloadedMesh.OpaqueIndices);
                }
            }
        }

        [Test]
        public void BuildJob_IsBurstCompiledAndContainsNoUnityEngineFields()
        {
            var type = typeof(ChunkMeshBuildJob);
            Assert.That(
                type.GetCustomAttributes(false)
                    .Any(attribute => attribute.GetType().FullName == "Unity.Burst.BurstCompileAttribute"),
                Is.True);

            var unityEngineFields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType.Namespace != null &&
                                field.FieldType.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                .Select(field => field.Name)
                .ToArray();
            Assert.That(unityEngineFields, Is.Empty);
        }

        [Test]
        public void RendererPool_ReusesOneGameObjectForDifferentChunk()
        {
            var root = new GameObject("Chunk Renderer Pool Test");
            try
            {
                using (var pool = new ChunkRendererPool(root.transform, null))
                {
                    var first = pool.GetOrCreate(int3.zero);
                    Assert.That(pool.Release(int3.zero), Is.True);
                    var second = pool.GetOrCreate(new int3(-2, 3, 4));
                    Assert.That(second, Is.SameAs(first));
                    Assert.That(pool.CreatedCount, Is.EqualTo(1));
                    Assert.That(pool.ActiveCount, Is.EqualTo(1));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RenderPipeline_DeduplicatesVersionButRebuildsReusedHandleGeneration()
        {
            var root = new GameObject("Chunk Render Pipeline Test");
            try
            {
                using (var world = new WorldCore())
                using (var table = CreateTable(1))
                using (var pool = new ChunkRendererPool(root.transform, null))
                {
                    var pipeline = new ChunkRenderPipeline();
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();

                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.Zero);
                    Assert.That(pool.TryGet(int3.zero, out var firstRenderer), Is.True);
                    var firstHandle = firstRenderer.AppliedHandle;
                    Assert.That(firstRenderer.Mesh.vertexCount, Is.EqualTo(24));

                    Assert.That(world.TryUnloadChunk(int3.zero), Is.True);
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(pool.TryGet(int3.zero, out var reusedRenderer), Is.True);
                    Assert.That(reusedRenderer, Is.SameAs(firstRenderer));
                    Assert.That(reusedRenderer.AppliedHandle, Is.Not.EqualTo(firstHandle));
                    Assert.That(reusedRenderer.AppliedHandle.Generation,
                        Is.Not.EqualTo(firstHandle.Generation));
                    Assert.That(reusedRenderer.Mesh.vertexCount, Is.EqualTo(24));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RebuildAll_RendersResidentChunkAfterDirtyViewWasCleared()
        {
            var root = new GameObject("Chunk Render Attach Test");
            try
            {
                using (var world = new WorldCore())
                using (var table = CreateTable(1))
                using (var pool = new ChunkRendererPool(root.transform, null))
                {
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    world.Flush();
                    Assert.That(world.DirtyChunks.Length, Is.Zero);

                    var pipeline = new ChunkRenderPipeline();
                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.Zero);
                    Assert.That(pipeline.RebuildAllNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(pool.TryGet(int3.zero, out var renderer), Is.True);
                    Assert.That(renderer.Mesh.vertexCount, Is.EqualTo(24));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CollisionReadiness_RequiresCurrentHandleAndVersionToBeApplied()
        {
            var root = new GameObject("Chunk Collision Readiness Test");
            try
            {
                using (var world = new WorldCore())
                using (var table = CreateTable(1))
                using (var pool = new ChunkRendererPool(root.transform, null))
                {
                    var pipeline = new ChunkRenderPipeline();
                    Assert.That(
                        pipeline.AreAllLoadedChunkCollidersReady(world, pool),
                        Is.True,
                        "An empty world has no missing collision projections.");

                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    Assert.That(pipeline.AreAllLoadedChunkCollidersReady(world, pool), Is.False);

                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(pipeline.AreAllLoadedChunkCollidersReady(world, pool), Is.True);

                    world.Enqueue(WorldCommand.SetBlock(new int3(1, 0, 0), new Block(1, 0)));
                    world.Flush();
                    Assert.That(
                        pipeline.AreAllLoadedChunkCollidersReady(world, pool),
                        Is.False,
                        "A previous collider version must not release the readiness barrier.");

                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(pipeline.AreAllLoadedChunkCollidersReady(world, pool), Is.True);

                    Assert.That(world.TryUnloadChunk(int3.zero), Is.True);
                    world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 0)));
                    world.Flush();
                    Assert.That(pipeline.AreAllLoadedChunkCollidersReady(world, pool), Is.False);
                    Assert.That(pipeline.RebuildDirtyNow(world, table, pool), Is.EqualTo(1));
                    Assert.That(
                        pipeline.AreAllLoadedChunkCollidersReady(world, pool),
                        Is.True,
                        "A pooled renderer must reset readiness for a recycled handle generation.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static ChunkMeshBuildRequest Schedule(
            WorldCore world,
            ChunkHandle handle,
            BlockRenderTable table)
        {
            Assert.That(ChunkMeshBuilder.TrySchedule(world, handle, table, out var request), Is.True);
            return request;
        }

        private static BlockRenderTable CreateTable(uint id)
        {
            var face = CreateFace(BlockRenderPass.Opaque);
            return new BlockRenderTable(
                new[]
                {
                    new BlockRenderDefinition
                    {
                        Id = id,
                        GeometryKind = BlockGeometryKind.Cube,
                        North = face,
                        South = face,
                        East = face,
                        West = face,
                        Up = face,
                        Down = face
                    }
                });
        }

        private static BlockFaceRenderDefinition CreateFace(BlockRenderPass pass)
        {
            return new BlockFaceRenderDefinition(
                new float2(0f, 0f),
                new float2(0f, 1f),
                new float2(1f, 1f),
                new float2(1f, 0f),
                pass);
        }

        private static BlockRenderManifest.Entry FindEntry(
            BlockRenderManifest manifest,
            string canonicalName)
        {
            return manifest.Entries.Single(entry => entry.CanonicalName == canonicalName);
        }

        private static void AssertEntryUvsMatchPrefab(
            BlockRegistryManifest registry,
            BlockRenderManifest.Entry renderEntry)
        {
            var registryEntry = registry.Entries.Single(entry =>
                entry.CanonicalName == renderEntry.CanonicalName);
            var meshFilter = registryEntry.Prefab.GetComponent<MeshFilter>();
            Assert.That(meshFilter, Is.Not.Null, renderEntry.CanonicalName);
            Assert.That(meshFilter.sharedMesh, Is.Not.Null, renderEntry.CanonicalName);
            var sourceUvs = meshFilter.sharedMesh.uv;
            Assert.That(sourceUvs.Length, Is.EqualTo(24), renderEntry.CanonicalName);

            for (var faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                var face = renderEntry.Faces[faceIndex];
                var sourceOffset = faceIndex * 4;
                Assert.That(face.Uv0, Is.EqualTo(sourceUvs[sourceOffset]));
                Assert.That(face.Uv1, Is.EqualTo(sourceUvs[sourceOffset + 1]));
                Assert.That(face.Uv2, Is.EqualTo(sourceUvs[sourceOffset + 2]));
                Assert.That(face.Uv3, Is.EqualTo(sourceUvs[sourceOffset + 3]));
            }
        }

        private static void AssertHalfTexel(Vector2 uv, int atlasSize)
        {
            Assert.That(Mathf.Repeat(uv.x * atlasSize, 1f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Mathf.Repeat(uv.y * atlasSize, 1f), Is.EqualTo(0.5f).Within(0.0001f));
        }

        private static Vector2 ToVector2(float2 value)
        {
            return new Vector2(value.x, value.y);
        }

        private static WorldCore BuildWorld(BlockRecord[] records)
        {
            var world = new WorldCore();
            for (var i = 0; i < records.Length; i++)
                world.Enqueue(WorldCommand.SetBlock(records[i].Position, records[i].Block));
            world.Flush();
            return world;
        }

        private static void AssertNativeArraysEqual<T>(NativeArray<T> left, NativeArray<T> right)
            where T : struct
        {
            Assert.That(right.Length, Is.EqualTo(left.Length));
            for (var i = 0; i < left.Length; i++)
                Assert.That(right[i], Is.EqualTo(left[i]), $"Native array differs at index {i}.");
        }

        private readonly struct BlockRecord
        {
            public readonly int3 Position;
            public readonly Block Block;

            public BlockRecord(int3 position, Block block)
            {
                Position = position;
                Block = block;
            }
        }

        [Serializable]
        private sealed class SerializedBlockSet
        {
            public SerializedBlockRecord[] records;
        }

        [Serializable]
        private struct SerializedBlockRecord
        {
            public int x;
            public int y;
            public int z;
            public uint id;
            public uint state;
        }
    }
}
