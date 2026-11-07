using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Storage;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class WorldReplicaReadinessTests
    {
        [Test]
        public void CollisionReadiness_IsVersionedAndResetByTheNextWorldDelta()
        {
            using var state = new ClientStateCoordinator();
            var sessionId = new SessionId(Guid.NewGuid());
            var events = new List<ClientWorldReadiness>();
            state.WorldCollisionReady += events.Add;
            Assert.That(
                state.ApplySnapshot(
                    sessionId,
                    1,
                    4,
                    new WorldStorageSnapshot(Array.Empty<StoredChunk>())),
                Is.True);
            Assert.That(state.TryGetWorld(out var world), Is.True);

            state.MarkCollisionReady(world);
            state.MarkCollisionReady(world);

            Assert.That(state.IsWorldCollisionReady, Is.True);
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0].TickIndex, Is.EqualTo(4));

            state.MarkCollisionUnavailable(world);
            Assert.That(state.IsWorldCollisionReady, Is.False);
            state.MarkCollisionReady(world);
            Assert.That(events, Has.Count.EqualTo(2));

            Assert.That(
                state.ApplyDelta(
                    sessionId,
                    1,
                    5,
                    new[]
                    {
                        new WorldBlockDelta(int3.zero, Block.Air, new Block(1))
                    }),
                Is.True);
            Assert.That(state.IsWorldCollisionReady, Is.False);

            state.MarkCollisionReady(world);
            Assert.That(state.IsWorldCollisionReady, Is.True);
            Assert.That(events, Has.Count.EqualTo(3));
            Assert.That(events[2].TickIndex, Is.EqualTo(5));
        }
    }
}
