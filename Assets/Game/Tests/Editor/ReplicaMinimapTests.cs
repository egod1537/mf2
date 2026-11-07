using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.UI;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace MF2.Tests
{
    public sealed class ReplicaMinimapTests
    {
        [Test]
        public void SurfaceSampling_UsesHighestReplicaBlock()
        {
            var world = new FakeReplica();
            world.Set(new int3(3, 1, -2), new Block(11));
            world.Set(new int3(3, 5, -2), new Block(22));

            var result = ReplicaMinimapController.FindSurfaceBlock(world, 3, -2, -4, 8);

            Assert.That(result.Id, Is.EqualTo(22));
            Assert.That(world.Reads, Is.LessThanOrEqualTo(13));
        }

        [Test]
        public void SurfaceSampling_EmptyColumnReturnsAirWithinFixedBudget()
        {
            var world = new FakeReplica();
            var result = ReplicaMinimapController.FindSurfaceBlock(world, 0, 0, -2, 2);
            Assert.That(result.IsAir, Is.True);
            Assert.That(world.Reads, Is.EqualTo(5));
        }

        private sealed class FakeReplica : IClientReplicatedWorld
        {
            private readonly Dictionary<int3, Block> blocks = new Dictionary<int3, Block>();
            public event Action Changed;
            public bool HasSession => true;
            public SessionId SessionId => new SessionId(Guid.Parse("91d957ef-421d-44f8-a285-b50267196f27"));
            public long Generation => 1;
            public long TickIndex => 1;
            public int Reads { get; private set; }

            public Block GetBlockOrAir(int3 position)
            {
                Reads++;
                return blocks.TryGetValue(position, out var block) ? block : Block.Air;
            }

            public void Set(int3 position, Block block)
            {
                blocks[position] = block;
                Changed?.Invoke();
            }
        }
    }
}
