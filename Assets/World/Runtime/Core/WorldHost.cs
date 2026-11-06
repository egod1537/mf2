using System;
using System.Collections.Generic;
using MF2.World.Registry;
using MF2.World.Rendering;
using MF2.World.Storage;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MF2.World.Core
{
    /// <summary>
    /// Scene bridge for the native world lifetime. Scene YAML stores only managed block records
    /// and asset references; every NativeContainer remains owned by WorldCore/BlockRenderTable.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("MF2/World Host")]
    public sealed class WorldHost : MonoBehaviour
    {
        private static readonly List<WorldHost> ActiveHosts = new List<WorldHost>();

        [SerializeField] private BlockRegistryManifest blockRegistryManifest;
        [SerializeField] private BlockRenderManifest blockRenderManifest;
        [SerializeField] private ChunkMaterialSet chunkMaterialSet;
        [SerializeField, HideInInspector]
        private List<SerializedBlockRecord> serializedBlocks = new List<SerializedBlockRecord>();

        private readonly Dictionary<int3, int> serializedIndices =
            new Dictionary<int3, int>();
        private readonly WorldStorageSession storageSession = new WorldStorageSession();
        private WorldCore world;
        private BlockRenderTable renderTable;
        private ChunkRendererPool rendererPool;
        private ChunkRenderPipeline renderPipeline;
        private GameObject rendererRoot;
        private bool reinitializeRequested;

        public BlockRegistryManifest BlockRegistryManifest => blockRegistryManifest;
        public BlockRenderManifest BlockRenderManifest => blockRenderManifest;
        public ChunkMaterialSet ChunkMaterialSet => chunkMaterialSet;
        public bool IsInitialized => world != null;
        public int SerializedBlockCount => serializedBlocks != null ? serializedBlocks.Count : 0;
        public int DirtyStorageChunkCount => storageSession.DirtyChunkCount;
        internal int PendingCommandCount
        {
            get
            {
                EnsureInitialized();
                return world.PendingCommandCount;
            }
        }
        public bool HasEditorAssets =>
            blockRegistryManifest != null &&
            blockRenderManifest != null &&
            chunkMaterialSet != null;

        public bool IsGridTransformValid =>
            Quaternion.Angle(transform.rotation, Quaternion.identity) < 0.001f &&
            ApproximatelyOne(transform.lossyScale.x) &&
            ApproximatelyOne(transform.lossyScale.y) &&
            ApproximatelyOne(transform.lossyScale.z);

        public static bool TryGetActiveHost(out WorldHost host)
        {
            var activeScene = SceneManager.GetActiveScene();
            for (var i = ActiveHosts.Count - 1; i >= 0; i--)
            {
                var candidate = ActiveHosts[i];
                if (candidate == null)
                {
                    ActiveHosts.RemoveAt(i);
                    continue;
                }

                if (candidate.isActiveAndEnabled && candidate.gameObject.scene == activeScene)
                {
                    host = candidate;
                    return true;
                }
            }

            host = null;
            return false;
        }

        public static void DisposeAllActiveWorlds()
        {
            for (var i = ActiveHosts.Count - 1; i >= 0; i--)
            {
                if (ActiveHosts[i] != null)
                    ActiveHosts[i].DisposeWorld();
            }
        }

        public void Enqueue(WorldCommand command)
        {
            EnsureInitialized();
            world.Enqueue(command);
        }

        public FlushResult Flush()
        {
            EnsureInitialized();
            var result = world.Flush();
            storageSession.TrackDirty(world);
            ApplySerializedChanges();
            if (renderTable != null && rendererPool != null)
                renderPipeline.RebuildDirtyNow(world, renderTable, rendererPool);
            return result;
        }

        /// <summary>
        /// Synchronous manual save. Invoke from an explicit command/button callback, not an
        /// EditorWindow Repaint event. Unloaded chunks already present on disk are preserved.
        /// </summary>
        public WorldSaveResult SaveWorld(string directory)
        {
            EnsureInitialized();
            var storage = new RegionJsonWorldStorage(
                directory,
                StoredBlockValidator.FromManifest(blockRegistryManifest));
            return storageSession.SaveDirty(world, storage);
        }

        /// <summary>
        /// Parses and validates all region files, builds a replacement WorldCore, then swaps it
        /// into this host. Corrupt JSON never mutates the current live world partially.
        /// </summary>
        public WorldStorageSnapshot LoadWorld(string directory)
        {
            EnsureInitialized();
            var storage = new RegionJsonWorldStorage(
                directory,
                StoredBlockValidator.FromManifest(blockRegistryManifest));
            var snapshot = storage.LoadAll();
            var replacement = WorldStorageWorldBuilder.CreateWorld(snapshot);
            try
            {
                DisposeWorld();
                ReplaceSerializedBlocks(snapshot);
                world = replacement;
                replacement = null;
                CreateRenderOwnership();
                if (renderTable != null && rendererPool != null && world.DirtyChunks.Length > 0)
                    renderPipeline.RebuildDirtyNow(world, renderTable, rendererPool);
                storageSession.Clear();
                return snapshot;
            }
            catch
            {
                replacement?.Dispose();
                DisposeWorld();
                throw;
            }
        }

        public bool TryGetBlock(int3 worldPosition, out Block block)
        {
            EnsureInitialized();
            return world.TryGetBlock(worldPosition, out block);
        }

        public Block GetBlockOrAir(int3 worldPosition)
        {
            EnsureInitialized();
            return world.GetBlockOrAir(worldPosition);
        }

        public void DisposeWorld()
        {
            rendererPool?.Dispose();
            rendererPool = null;
            renderPipeline = null;

            if (rendererRoot != null)
            {
                DestroyOwnedObject(rendererRoot);
                rendererRoot = null;
            }

            renderTable?.Dispose();
            renderTable = null;
            world?.Dispose();
            world = null;
            storageSession.Clear();
        }

        internal void ConfigureAssets(
            BlockRegistryManifest registryManifest,
            BlockRenderManifest renderManifest,
            ChunkMaterialSet materialSet)
        {
            blockRegistryManifest = registryManifest;
            blockRenderManifest = renderManifest;
            chunkMaterialSet = materialSet;
            reinitializeRequested = true;
            if (isActiveAndEnabled)
                Reinitialize();
        }

        internal void Reinitialize()
        {
            DisposeWorld();
            reinitializeRequested = false;
            EnsureInitialized();
        }

        private void OnEnable()
        {
            if (!ActiveHosts.Contains(this))
                ActiveHosts.Add(this);
            reinitializeRequested = false;
            EnsureInitialized();
        }

        private void OnDisable()
        {
            ActiveHosts.Remove(this);
            DisposeWorld();
        }

        private void OnDestroy()
        {
            ActiveHosts.Remove(this);
            DisposeWorld();
        }

        private void OnValidate()
        {
            reinitializeRequested = true;
        }

        private void Update()
        {
            if (reinitializeRequested && isActiveAndEnabled)
                Reinitialize();
        }

        private void EnsureInitialized()
        {
            if (world != null)
                return;

            NormalizeSerializedBlocks();
            world = new WorldCore(
                initialChunkCapacity: Math.Max(4, serializedBlocks.Count / ChunkCoordinates.Volume + 1),
                initialCommandCapacity: Math.Max(64, serializedBlocks.Count));
            try
            {
                CreateRenderOwnership();

                for (var i = 0; i < serializedBlocks.Count; i++)
                {
                    var record = serializedBlocks[i];
                    world.Enqueue(WorldCommand.SetBlock(record.Position, record.Block));
                }

                if (serializedBlocks.Count > 0)
                {
                    world.Flush();
                    storageSession.TrackDirty(world);
                    if (renderTable != null && rendererPool != null)
                        renderPipeline.RebuildDirtyNow(world, renderTable, rendererPool);
                }
            }
            catch
            {
                DisposeWorld();
                throw;
            }
        }

        private void CreateRenderOwnership()
        {
            if (blockRenderManifest == null || chunkMaterialSet == null)
                return;

            renderTable = blockRenderManifest.CreateRuntimeTable();
            rendererRoot = new GameObject("World Chunk Renderers")
            {
                hideFlags = HideFlags.HideInHierarchy |
                            HideFlags.DontSaveInEditor |
                            HideFlags.DontSaveInBuild
            };
            rendererRoot.transform.SetParent(transform, false);
            rendererPool = new ChunkRendererPool(
                rendererRoot.transform,
                chunkMaterialSet,
                HideFlags.HideInHierarchy |
                HideFlags.DontSaveInEditor |
                HideFlags.DontSaveInBuild);
            renderPipeline = new ChunkRenderPipeline();
        }

        private void ReplaceSerializedBlocks(WorldStorageSnapshot snapshot)
        {
            serializedBlocks.Clear();
            serializedIndices.Clear();
            for (var chunkIndex = 0; chunkIndex < snapshot.Chunks.Count; chunkIndex++)
            {
                var chunk = snapshot.Chunks[chunkIndex];
                for (var blockIndex = 0; blockIndex < chunk.Blocks.Count; blockIndex++)
                {
                    var storedBlock = chunk.Blocks[blockIndex];
                    var position = RegionCoordinates.ComposeWorldPosition(
                        chunk.ChunkCoordinate,
                        storedBlock.Index);
                    serializedIndices.Add(position, serializedBlocks.Count);
                    serializedBlocks.Add(new SerializedBlockRecord(position, storedBlock.ToBlock()));
                }
            }

            NormalizeSerializedBlocks();
        }

        private void NormalizeSerializedBlocks()
        {
            if (serializedBlocks == null)
                serializedBlocks = new List<SerializedBlockRecord>();

            var unique = new Dictionary<int3, Block>(serializedBlocks.Count);
            for (var i = 0; i < serializedBlocks.Count; i++)
            {
                var record = serializedBlocks[i];
                if (!record.Block.IsAir)
                    unique[record.Position] = record.Block;
            }

            var positions = new List<int3>(unique.Keys);
            positions.Sort(CompareCoordinates);
            serializedBlocks.Clear();
            serializedIndices.Clear();
            for (var i = 0; i < positions.Count; i++)
            {
                var position = positions[i];
                serializedBlocks.Add(new SerializedBlockRecord(position, unique[position]));
                serializedIndices.Add(position, i);
            }
        }

        private void ApplySerializedChanges()
        {
            var changes = world.Changes;
            for (var i = 0; i < changes.Length; i++)
            {
                var change = changes[i];
                if (change.Original == change.Final)
                    continue;

                if (change.Final.IsAir)
                {
                    RemoveSerializedBlock(change.WorldPosition);
                    continue;
                }

                if (serializedIndices.TryGetValue(change.WorldPosition, out var index))
                {
                    serializedBlocks[index] =
                        new SerializedBlockRecord(change.WorldPosition, change.Final);
                }
                else
                {
                    serializedIndices.Add(change.WorldPosition, serializedBlocks.Count);
                    serializedBlocks.Add(
                        new SerializedBlockRecord(change.WorldPosition, change.Final));
                }
            }
        }

        private void RemoveSerializedBlock(int3 position)
        {
            if (!serializedIndices.TryGetValue(position, out var index))
                return;

            var lastIndex = serializedBlocks.Count - 1;
            if (index != lastIndex)
            {
                var moved = serializedBlocks[lastIndex];
                serializedBlocks[index] = moved;
                serializedIndices[moved.Position] = index;
            }

            serializedBlocks.RemoveAt(lastIndex);
            serializedIndices.Remove(position);
        }

        private static int CompareCoordinates(int3 left, int3 right)
        {
            var x = left.x.CompareTo(right.x);
            if (x != 0)
                return x;
            var y = left.y.CompareTo(right.y);
            return y != 0 ? y : left.z.CompareTo(right.z);
        }

        private static bool ApproximatelyOne(float value)
        {
            return Mathf.Abs(value - 1f) < 0.0001f;
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        [Serializable]
        private struct SerializedBlockRecord
        {
            [SerializeField] private int x;
            [SerializeField] private int y;
            [SerializeField] private int z;
            [SerializeField] private uint id;
            [SerializeField] private uint state;

            public int3 Position => new int3(x, y, z);
            public Block Block => new Block(id, state);

            public SerializedBlockRecord(int3 position, Block block)
            {
                x = position.x;
                y = position.y;
                z = position.z;
                id = block.Id;
                state = block.State;
            }
        }
    }
}
