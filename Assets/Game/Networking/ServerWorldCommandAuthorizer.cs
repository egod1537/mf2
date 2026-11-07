using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using MF2.World.Core;
using MF2.World.Registry;
using Unity.Mathematics;
using VContainer;

namespace MF2.Game.Networking
{
    /// <summary>Managed stable-ID catalog built from the Unity authoring manifest at app startup.</summary>
    public sealed class ManifestBlockIdCatalog : IBlockIdCatalog
    {
        private readonly HashSet<uint> ids;

        public ManifestBlockIdCatalog(BlockRegistryManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            ids = new HashSet<uint>();
            for (var i = 0; i < manifest.Entries.Count; i++)
                ids.Add(StableBlockId.Compute(manifest.Entries[i].CanonicalName));
        }

        public bool IsKnown(uint stableBlockId) =>
            stableBlockId != 0 && ids.Contains(stableBlockId);
    }

    /// <summary>
    /// Network-player command policy. Connection identity is mapped before this point; the policy
    /// additionally requires a live server body, a known stable block ID, and reachable bounds.
    /// </summary>
    public sealed class ServerWorldCommandAuthorizer : IGameCommandAuthorizer
    {
        public const float DefaultReach = 8f;

        private readonly IPhysicsQuery physics;
        private readonly IBlockIdCatalog blocks;
        private readonly double reachSquared;

        [Inject]
        public ServerWorldCommandAuthorizer(IPhysicsQuery physics, IBlockIdCatalog blocks)
            : this(physics, blocks, DefaultReach)
        {
        }

        internal ServerWorldCommandAuthorizer(
            IPhysicsQuery physics,
            IBlockIdCatalog blocks,
            float reach)
        {
            this.physics = physics ?? throw new ArgumentNullException(nameof(physics));
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            if (reach <= 0f || float.IsNaN(reach) || float.IsInfinity(reach))
                throw new ArgumentOutOfRangeException(nameof(reach));
            reachSquared = (double)reach * reach;
        }

        public bool IsAuthorized(PlayerId sender, in GameCommand command)
        {
            if (command.Type == GameCommandType.Inventory)
                return command.InventoryCommand.IsStructurallyValid;
            if (command.Type == GameCommandType.PlayerAction)
                return IsPlayerActionAuthorized(sender, command.PlayerActionCommand);
            if (command.Type != GameCommandType.World || !physics.TryGetBody(sender, out var body))
            {
                return false;
            }

            var world = command.WorldCommand;
            if (!world.Block.IsAir && !blocks.IsKnown(world.Block.Id))
                return false;

            var x = DistanceToRange(body.Position.x, world.Min.x, world.Max.x);
            var y = DistanceToRange(body.Position.y, world.Min.y, world.Max.y);
            var z = DistanceToRange(body.Position.z, world.Min.z, world.Max.z);
            return x * x + y * y + z * z <= reachSquared;
        }

        private bool IsPlayerActionAuthorized(
            PlayerId sender,
            in PlayerActionCommand command)
        {
            if (!command.IsStructurallyValid ||
                !physics.TryGetBody(sender, out var attacker))
            {
                return false;
            }
            if (command.Kind == PlayerActionCommandKind.Dash)
                return true;
            if (command.Kind != PlayerActionCommandKind.Attack ||
                command.Target == sender ||
                !physics.TryGetBody(command.Target, out var target))
            {
                return false;
            }

            var distance = math.distance(attacker.Position, target.Position);
            // The authenticated network ingress performs the collider LOS check before creating
            // this identity-bearing command. This policy independently fences registered bodies
            // and authoritative distance without exposing a client-supplied target id.
            return math.isfinite(distance) && distance > 0.01f && distance <= 2.5f;
        }

        private static double DistanceToRange(float value, int minimum, int maximum)
        {
            if (value < minimum)
                return (double)minimum - value;
            if (value > maximum)
                return (double)value - maximum;
            return 0d;
        }
    }
}
