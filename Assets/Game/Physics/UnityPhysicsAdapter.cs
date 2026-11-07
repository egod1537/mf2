using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using Unity.Mathematics;
using UnityEngine;
using VContainer;
using DomainRayHit = MF2.Game.Domain.RayHit;

namespace MF2.Game.Physics
{
    /// <summary>Server-session registration boundary for Unity physics objects.</summary>
    public interface IPhysicsBodyRegistry
    {
        int RegisteredBodyCount { get; }
        bool TryRegisterBody(PlayerId entityId, Rigidbody body);
        bool UnregisterBody(PlayerId entityId, Rigidbody expectedBody = null);
    }

    /// <summary>
    /// Session-scoped Unity adapter. It reads the PhysicsScene but never advances it; FishNet's
    /// TimeManager remains the sole caller of Physics.Simulate.
    /// </summary>
    public sealed class UnityPhysicsAdapter :
        IPhysicsQuery,
        IPhysicsCommands,
        IPhysicsBodyRegistry,
        IDisposable
    {
        private sealed class BodyRegistration
        {
            public BodyRegistration(Rigidbody body, Collider[] colliders)
            {
                Body = body;
                Colliders = colliders;
            }

            public Rigidbody Body { get; }
            public Collider[] Colliders { get; }
        }

        private readonly Dictionary<PlayerId, BodyRegistration> bodiesByEntity =
            new Dictionary<PlayerId, BodyRegistration>();
        private readonly Dictionary<Rigidbody, PlayerId> entitiesByBody =
            new Dictionary<Rigidbody, PlayerId>();
        private readonly Dictionary<Collider, PlayerId> entitiesByCollider =
            new Dictionary<Collider, PlayerId>();
        private readonly PhysicsScene physicsScene;
        private bool disposed;

        [Inject]
        public UnityPhysicsAdapter()
            : this(UnityEngine.Physics.defaultPhysicsScene)
        {
        }

        internal UnityPhysicsAdapter(PhysicsScene physicsScene)
        {
            this.physicsScene = physicsScene;
        }

        public int RegisteredBodyCount => bodiesByEntity.Count;

        public bool TryRegisterBody(PlayerId entityId, Rigidbody body)
        {
            ThrowIfDisposed();
            if (entityId.Value == Guid.Empty || body == null)
                return false;
            if (bodiesByEntity.TryGetValue(entityId, out var existing))
                return ReferenceEquals(existing.Body, body);
            if (entitiesByBody.TryGetValue(body, out var existingEntity) &&
                existingEntity != entityId)
            {
                return false;
            }

            var colliders = body.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider != null &&
                    entitiesByCollider.TryGetValue(collider, out existingEntity) &&
                    existingEntity != entityId)
                {
                    return false;
                }
            }

            bodiesByEntity.Add(entityId, new BodyRegistration(body, colliders));
            entitiesByBody.Add(body, entityId);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    entitiesByCollider[colliders[i]] = entityId;
            }
            return true;
        }

        public bool UnregisterBody(PlayerId entityId, Rigidbody expectedBody = null)
        {
            if (disposed || !bodiesByEntity.TryGetValue(entityId, out var registration))
                return false;
            if (expectedBody != null && !ReferenceEquals(registration.Body, expectedBody))
                return false;

            bodiesByEntity.Remove(entityId);
            entitiesByBody.Remove(registration.Body);
            for (var i = 0; i < registration.Colliders.Length; i++)
            {
                var collider = registration.Colliders[i];
                if (collider != null &&
                    entitiesByCollider.TryGetValue(collider, out var mapped) &&
                    mapped == entityId)
                {
                    entitiesByCollider.Remove(collider);
                }
            }
            return true;
        }

        public bool TryGetBody(PlayerId entityId, out PhysicsBodySnapshot snapshot)
        {
            ThrowIfDisposed();
            snapshot = default;
            if (!bodiesByEntity.TryGetValue(entityId, out var registration))
                return false;
            var body = registration.Body;
            if (body == null)
            {
                UnregisterBody(entityId);
                return false;
            }

            var position = body.position;
            var rotation = body.rotation;
            var velocity = body.linearVelocity;
            var angularVelocity = body.angularVelocity;
            snapshot = new PhysicsBodySnapshot(
                entityId,
                new float3(position.x, position.y, position.z),
                new quaternion(rotation.x, rotation.y, rotation.z, rotation.w),
                new float3(velocity.x, velocity.y, velocity.z),
                new float3(angularVelocity.x, angularVelocity.y, angularVelocity.z),
                body.isKinematic,
                body.IsSleeping());
            return true;
        }

        public bool Raycast(in RayQuery query, out DomainRayHit hit)
        {
            ThrowIfDisposed();
            hit = default;
            if (!physicsScene.IsValid())
                return false;

            var origin = new Vector3(query.Origin.x, query.Origin.y, query.Origin.z);
            var direction = new Vector3(query.Direction.x, query.Direction.y, query.Direction.z);
            var triggerInteraction = query.IncludeTriggers
                ? QueryTriggerInteraction.Collide
                : QueryTriggerInteraction.Ignore;
            if (!physicsScene.Raycast(
                    origin,
                    direction,
                    out var unityHit,
                    query.Distance,
                    query.LayerMask,
                    triggerInteraction))
            {
                return false;
            }

            var point = unityHit.point;
            var normal = unityHit.normal;
            var entityId = default(PlayerId);
            if (unityHit.collider != null)
                entitiesByCollider.TryGetValue(unityHit.collider, out entityId);
            hit = new DomainRayHit(
                entityId,
                new float3(point.x, point.y, point.z),
                new float3(normal.x, normal.y, normal.z),
                unityHit.distance);
            return true;
        }

        public bool ApplyForce(PlayerId entityId, float3 force)
        {
            ThrowIfDisposed();
            if (!math.all(math.isfinite(force)) ||
                !TryGetLiveBody(entityId, out var body))
            {
                return false;
            }

            body.AddForce(new Vector3(force.x, force.y, force.z), ForceMode.Force);
            return true;
        }

        public bool Teleport(PlayerId entityId, float3 position, quaternion rotation)
        {
            ThrowIfDisposed();
            if (!math.all(math.isfinite(position)) ||
                !math.all(math.isfinite(rotation.value)) ||
                math.lengthsq(rotation.value) < 0.000001f ||
                !TryGetLiveBody(entityId, out var body))
            {
                return false;
            }

            var normalized = math.normalize(rotation);
            body.position = new Vector3(position.x, position.y, position.z);
            body.rotation = new Quaternion(
                normalized.value.x,
                normalized.value.y,
                normalized.value.z,
                normalized.value.w);
            return true;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            bodiesByEntity.Clear();
            entitiesByBody.Clear();
            entitiesByCollider.Clear();
            disposed = true;
        }

        private bool TryGetLiveBody(PlayerId entityId, out Rigidbody body)
        {
            body = null;
            if (!bodiesByEntity.TryGetValue(entityId, out var registration))
                return false;
            body = registration.Body;
            if (body != null)
                return true;
            UnregisterBody(entityId);
            body = null;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(UnityPhysicsAdapter));
        }
    }
}
