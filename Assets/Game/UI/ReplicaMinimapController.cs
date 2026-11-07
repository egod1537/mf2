using MF2.Entities.Player;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Registry;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace MF2.Game.UI
{
    /// <summary>Bounded top-down projection of the client replica; never reads server state.</summary>
    [DisallowMultipleComponent]
    public sealed class ReplicaMinimapController : MonoBehaviour
    {
        public const int MapSize = 49;
        public const int MinimumSampleY = -32;
        public const int MaximumSampleY = 64;

        private static readonly uint Grass = StableBlockId.Compute("minecraft:grass_block");
        private static readonly uint Dirt = StableBlockId.Compute("minecraft:dirt");
        private static readonly uint Stone = StableBlockId.Compute("minecraft:stone");
        private static readonly uint Water = StableBlockId.Compute("minecraft:water");

        [SerializeField] private RawImage mapImage;
        private IClientReplicatedWorld replica;
        private Texture2D texture;
        private Color32[] terrainPixels;
        private Color32[] displayPixels;
        private BlockPlayerController owner;
        private int2 lastCenter = new int2(int.MinValue, int.MinValue);
        private Vector2 lastFacing;
        private bool terrainDirty = true;

        [Inject]
        private void Construct(IClientReplicatedWorld clientReplica) => replica = clientReplica;

        public void Configure(RawImage image) => mapImage = image;

        private void OnEnable()
        {
            EnsureTexture();
            if (replica != null)
                replica.Changed += OnReplicaChanged;
            terrainDirty = true;
        }

        private void OnDisable()
        {
            if (replica != null)
                replica.Changed -= OnReplicaChanged;
            owner = null;
        }

        private void LateUpdate()
        {
            if (replica == null || !replica.HasSession)
                return;
            if (owner == null || !owner.IsOwner)
                owner = FindOwner();
            if (owner == null)
                return;

            var position = owner.transform.position;
            var center = new int2(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.z));
            var facing = owner.CurrentWorldFacing;
            var rebuilt = false;
            if (terrainDirty || !center.Equals(lastCenter))
            {
                RebuildTerrain(center);
                lastCenter = center;
                terrainDirty = false;
                rebuilt = true;
            }
            if (terrainPixels == null || (!rebuilt && facing == lastFacing))
            {
                return;
            }
            DrawPlayer(facing);
            lastFacing = facing;
        }

        private void RebuildTerrain(int2 center)
        {
            EnsureTexture();
            var radius = MapSize / 2;
            for (var z = 0; z < MapSize; z++)
            for (var x = 0; x < MapSize; x++)
            {
                var worldX = center.x + x - radius;
                var worldZ = center.y + z - radius;
                var block = FindSurfaceBlock(replica, worldX, worldZ, MinimumSampleY, MaximumSampleY);
                terrainPixels[z * MapSize + x] = ToColor(block);
            }
        }

        private void DrawPlayer(Vector2 facing)
        {
            terrainPixels.CopyTo(displayPixels, 0);
            var center = MapSize / 2;
            displayPixels[center * MapSize + center] = new Color32(255, 240, 90, 255);
            if (facing.sqrMagnitude > 0.01f)
            {
                facing.Normalize();
                for (var distance = 1; distance <= 3; distance++)
                {
                    var x = center + Mathf.RoundToInt(facing.x * distance);
                    var z = center + Mathf.RoundToInt(facing.y * distance);
                    if ((uint)x < (uint)MapSize && (uint)z < (uint)MapSize)
                        displayPixels[z * MapSize + x] = new Color32(255, 170, 40, 255);
                }
            }
            texture.SetPixels32(displayPixels);
            texture.Apply(false, false);
        }

        public static Block FindSurfaceBlock(
            IClientReplicatedWorld world,
            int x,
            int z,
            int minimumY,
            int maximumY)
        {
            if (world == null)
                return Block.Air;
            for (var y = maximumY; y >= minimumY; y--)
            {
                var block = world.GetBlockOrAir(new int3(x, y, z));
                if (!block.IsAir)
                    return block;
            }
            return Block.Air;
        }

        private static Color32 ToColor(Block block)
        {
            if (block.IsAir)
                return new Color32(8, 13, 20, 230);
            if (block.Id == Grass)
                return new Color32(83, 145, 65, 255);
            if (block.Id == Dirt)
                return new Color32(118, 82, 55, 255);
            if (block.Id == Stone)
                return new Color32(112, 116, 123, 255);
            if (block.Id == Water)
                return new Color32(40, 105, 190, 245);
            return new Color32(175, 160, 125, 255);
        }

        private void EnsureTexture()
        {
            if (texture != null)
                return;
            texture = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false)
            {
                name = "Replica Minimap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            terrainPixels = new Color32[MapSize * MapSize];
            displayPixels = new Color32[terrainPixels.Length];
            if (mapImage != null)
                mapImage.texture = texture;
        }

        private static BlockPlayerController FindOwner()
        {
            var players = FindObjectsByType<BlockPlayerController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < players.Length; i++)
                if (players[i].IsOwner)
                    return players[i];
            return null;
        }

        private void OnReplicaChanged() => terrainDirty = true;

        private void OnDestroy()
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
