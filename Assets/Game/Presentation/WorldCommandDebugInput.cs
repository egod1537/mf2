using MF2.Entities.Player;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.World.Core;
using MF2.World.Registry;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace MF2.Game.Presentation
{
    /// <summary>
    /// Minimal manual vertical slice: F6 toggles one nearby block through ICommandSender. The
    /// scene WorldHost displays only the replicated client result, never the server WorldCore.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldCommandDebugInput : MonoBehaviour
    {
        [SerializeField] private BlockRegistryManifest blockRegistryManifest;
        [SerializeField] private Vector3Int target = Vector3Int.zero;

        private IGameCommandSender commandSender;
        private IClientReplicatedWorld replicatedWorld;
        private uint debugBlockId;

        public void Configure(BlockRegistryManifest manifest, Vector3Int targetPosition)
        {
            blockRegistryManifest = manifest;
            target = targetPosition;
        }

        [Inject]
        private void Construct(
            IGameCommandSender sender,
            IClientReplicatedWorld clientWorld)
        {
            commandSender = sender;
            replicatedWorld = clientWorld;
        }

        private void Awake()
        {
            if (blockRegistryManifest != null && blockRegistryManifest.Entries.Count > 0)
            {
                debugBlockId = StableBlockId.Compute(
                    blockRegistryManifest.Entries[0].CanonicalName);
            }
        }

        private void Update()
        {
            if (Keyboard.current?.f6Key.wasPressedThisFrame != true ||
                commandSender == null ||
                replicatedWorld == null ||
                !replicatedWorld.HasSession ||
                debugBlockId == 0)
            {
                return;
            }

            var position = ResolveTargetPosition();
            var current = replicatedWorld.GetBlockOrAir(position);
            var command = current.IsAir
                ? WorldCommand.SetBlock(position, new Block(debugBlockId))
                : WorldCommand.RemoveBlock(position);
            if (!commandSender.SendWorldCommand(command))
                Debug.LogWarning("[MF2] F6 debug block command could not be sent.");
            else
                Debug.Log($"[MF2] F6 debug block command sent for {position}.");
        }

        private int3 ResolveTargetPosition()
        {
            var players = FindObjectsByType<BlockPlayerController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < players.Length; i++)
            {
                if (players[i].IsOwner)
                {
                    return TargetNearLocalPlayer(
                        players[i].transform.position,
                        players[i].transform.forward);
                }
            }

            return new int3(target.x, target.y, target.z);
        }

        private static int3 TargetNearLocalPlayer(Vector3 position, Vector3 forward)
        {
            var horizontal = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (horizontal.sqrMagnitude < 0.001f)
                horizontal = Vector3.forward;
            horizontal.Normalize();
            var adjacent = position + horizontal * 2f;
            return new int3(
                Mathf.FloorToInt(adjacent.x),
                Mathf.FloorToInt(position.y - 1f),
                Mathf.FloorToInt(adjacent.z));
        }
    }
}
