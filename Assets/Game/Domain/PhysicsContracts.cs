using System;
using Unity.Mathematics;

namespace MF2.Game.Domain
{
    /// <summary>Unity-free ray request used by authoritative domain systems.</summary>
    public readonly struct RayQuery
    {
        public RayQuery(
            float3 origin,
            float3 direction,
            float distance,
            int layerMask = ~0,
            bool includeTriggers = false)
        {
            if (!math.all(math.isfinite(origin)))
                throw new ArgumentOutOfRangeException(nameof(origin));
            if (!math.all(math.isfinite(direction)) || math.lengthsq(direction) < 0.000001f)
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (!math.isfinite(distance) || distance <= 0f)
                throw new ArgumentOutOfRangeException(nameof(distance));

            Origin = origin;
            Direction = math.normalize(direction);
            Distance = distance;
            LayerMask = layerMask;
            IncludeTriggers = includeTriggers;
        }

        public float3 Origin { get; }
        public float3 Direction { get; }
        public float Distance { get; }
        public int LayerMask { get; }
        public bool IncludeTriggers { get; }
    }

    /// <summary>A copied hit result. An empty EntityId means the collider belongs to the world.</summary>
    public readonly struct RayHit
    {
        public RayHit(
            PlayerId entityId,
            float3 point,
            float3 normal,
            float distance)
        {
            EntityId = entityId;
            Point = point;
            Normal = normal;
            Distance = distance;
        }

        public PlayerId EntityId { get; }
        public bool HasEntity => EntityId.Value != Guid.Empty;
        public float3 Point { get; }
        public float3 Normal { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// Read-only copy of a Unity physics body after the current physics step. It is not a second
    /// authoritative position store and is never written back to PlayerModel.
    /// </summary>
    public readonly struct PhysicsBodySnapshot
    {
        public PhysicsBodySnapshot(
            PlayerId entityId,
            float3 position,
            quaternion rotation,
            float3 linearVelocity,
            float3 angularVelocity,
            bool isKinematic,
            bool isSleeping)
        {
            EntityId = entityId;
            Position = position;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;
            IsKinematic = isKinematic;
            IsSleeping = isSleeping;
        }

        public PlayerId EntityId { get; }
        public float3 Position { get; }
        public quaternion Rotation { get; }
        public float3 LinearVelocity { get; }
        public float3 AngularVelocity { get; }
        public bool IsKinematic { get; }
        public bool IsSleeping { get; }
    }

    public interface IPhysicsQuery
    {
        bool Raycast(in RayQuery query, out RayHit hit);
        bool TryGetBody(PlayerId entityId, out PhysicsBodySnapshot snapshot);
    }

    public interface IPhysicsCommands
    {
        bool ApplyForce(PlayerId entityId, float3 force);
        bool Teleport(PlayerId entityId, float3 position, quaternion rotation);
    }
}
