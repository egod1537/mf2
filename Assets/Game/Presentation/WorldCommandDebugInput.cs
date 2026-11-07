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

            var position = new int3(target.x, target.y, target.z);
            var current = replicatedWorld.GetBlockOrAir(position);
            var command = current.IsAir
                ? WorldCommand.SetBlock(position, new Block(debugBlockId))
                : WorldCommand.RemoveBlock(position);
            if (!commandSender.SendWorldCommand(command))
                Debug.LogWarning("[MF2] F6 debug block command could not be sent.");
        }
    }
}
