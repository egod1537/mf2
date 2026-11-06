using System;
using System.Linq;
using MF2.World.Core;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace MF2.World.Tests
{
    public sealed class WorldCoreTests
    {
        [Test]
        public void Block_IsEightBytes_AndDefaultIsAir()
        {
            Assert.That(UnsafeUtility.SizeOf<Block>(), Is.EqualTo(8));
            Assert.That(default(Block), Is.EqualTo(Block.Air));
            Assert.That(default(Block).Id, Is.Zero);
            Assert.That(default(Block).State, Is.Zero);
            Assert.Throws<ArgumentException>(() => new Block(0, 1));
        }

        [TestCase(-1, -1, 15)]
        [TestCase(-16, -1, 0)]
        [TestCase(-17, -2, 15)]
        [TestCase(0, 0, 0)]
        [TestCase(15, 0, 15)]
        [TestCase(16, 1, 0)]
        public void Coordinates_UseFloorDivisionAndModulo(int world, int chunk, int local)
        {
            Assert.That(ChunkCoordinates.FloorDivide(world), Is.EqualTo(chunk));
            Assert.That(ChunkCoordinates.FloorModulo(world), Is.EqualTo(local));
        }

        [Test]
        public void Coordinates_MapSignedThreeDimensionalBoundaryVectors()
        {
            var negative = new int3(-1, -1, -1);
            ChunkCoordinates.Split(negative, out var negativeChunk, out var negativeLocal);
            Assert.That(negativeChunk, Is.EqualTo(new int3(-1, -1, -1)));
            Assert.That(negativeLocal, Is.EqualTo(new int3(15, 15, 15)));

            var positive = new int3(16, 16, 16);
            ChunkCoordinates.Split(positive, out var positiveChunk, out var positiveLocal);
            Assert.That(positiveChunk, Is.EqualTo(new int3(1, 1, 1)));
            Assert.That(positiveLocal, Is.EqualTo(int3.zero));
        }

        [Test]
        public void Linearization_IsXThenYThenZ_AndRoundTrips()
        {
            var local = new int3(15, 2, 3);
            var index = ChunkCoordinates.ToIndex(local);

            Assert.That(index, Is.EqualTo(15 + 16 * (2 + 16 * 3)));
            Assert.That(ChunkCoordinates.FromIndex(index), Is.EqualTo(local));
        }

        [Test]
        public void Linearization_RoundTripsEveryCellInChunk()
        {
            for (var index = 0; index < ChunkCoordinates.Volume; index++)
            {
                var local = ChunkCoordinates.FromIndex(index);
                Assert.That(ChunkCoordinates.ToIndex(local), Is.EqualTo(index));
            }
        }

        [Test]
        public void SetGetAndRemove_RetainIdAndStateAcrossNegativeBoundary()
        {
            using (var world = new WorldCore(initialChunkCapacity: 1))
            {
                var negative = new int3(-1, 2, -1);
                var positive = new int3(0, 2, 0);
                var first = new Block(0x12345678, 0xABCDEF01);
                var second = new Block(0x87654321, 17);

                world.Enqueue(WorldCommand.SetBlock(negative, first));
                world.Enqueue(WorldCommand.SetBlock(positive, second));
                world.Flush();

                Assert.That(ChunkCoordinates.ToChunk(negative), Is.EqualTo(new int3(-1, 0, -1)));
                Assert.That(ChunkCoordinates.ToLocal(negative), Is.EqualTo(new int3(15, 2, 15)));
                Assert.That(world.TryGetBlock(negative, out var loadedFirst), Is.True);
                Assert.That(loadedFirst, Is.EqualTo(first));
                Assert.That(world.TryGetBlock(positive, out var loadedSecond), Is.True);
                Assert.That(loadedSecond, Is.EqualTo(second));

                world.Enqueue(WorldCommand.RemoveBlock(negative));
                world.Flush();
                Assert.That(world.TryGetBlock(negative, out var removed), Is.True);
                Assert.That(removed, Is.EqualTo(Block.Air));
            }
        }

        [Test]
        public void PoolReuse_IncrementsGenerationAndRejectsStaleHandle()
        {
            using (var world = new WorldCore(initialChunkCapacity: 1))
            {
                world.Enqueue(WorldCommand.SetBlock(new int3(0, 0, 0), new Block(1)));
                world.Flush();
                Assert.That(world.TryGetChunkHandle(int3.zero, out var first), Is.True);
                Assert.That(world.TryUnloadChunk(int3.zero), Is.True);
                Assert.That(world.IsChunkHandleValid(first), Is.False);

                world.Enqueue(WorldCommand.SetBlock(new int3(16, 0, 0), new Block(2)));
                world.Flush();
                Assert.That(world.TryGetChunkHandle(new int3(1, 0, 0), out var second), Is.True);
                Assert.That(second.Index, Is.EqualTo(first.Index));
                Assert.That(second.Generation, Is.Not.EqualTo(first.Generation));
                Assert.That(world.IsChunkHandleValid(first), Is.False);
                Assert.That(world.IsChunkHandleValid(second), Is.True);
            }
        }

        [Test]
        public void Lease_PreventsUnload_AndCopiedLeaseCannotReleaseTwice()
        {
            using (var world = new WorldCore(initialChunkCapacity: 1))
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1)));
                world.Flush();
                world.TryGetChunkHandle(int3.zero, out var handle);

                Assert.That(world.TryAcquireChunkLease(handle, out var lease), Is.True);
                var copiedLease = lease;
                Assert.That(lease.Length, Is.EqualTo(ChunkCoordinates.Volume));
                Assert.That(lease.GetBlock(0), Is.EqualTo(new Block(1)));
                Assert.That(world.TryUnloadChunk(int3.zero), Is.False);
                Assert.That(world.ReleaseChunkLease(lease), Is.True);
                Assert.That(world.ReleaseChunkLease(copiedLease), Is.False);
                Assert.That(world.TryUnloadChunk(int3.zero), Is.True);
            }
        }

        [Test]
        public void Lease_PreventsFlushWritesUntilReleased()
        {
            using (var world = new WorldCore(initialChunkCapacity: 1))
            {
                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1)));
                world.Flush();
                world.TryGetChunkHandle(int3.zero, out var handle);
                Assert.That(world.TryAcquireChunkLease(handle, out var lease), Is.True);

                world.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(2, 9)));
                Assert.Throws<InvalidOperationException>(() => world.Flush());
                Assert.That(lease.GetBlock(0), Is.EqualTo(new Block(1)));
                Assert.That(world.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(1)));

                Assert.That(world.ReleaseChunkLease(lease), Is.True);
                var result = world.Flush();
                Assert.That(result.ChangedCells, Is.EqualTo(1));
                Assert.That(world.GetBlockOrAir(int3.zero), Is.EqualTo(new Block(2, 9)));
            }
        }

        [Test]
        public void RepeatedWrites_CoalesceDirtyAndCaptureOriginalFinalOnce()
        {
            using (var world = new WorldCore())
            {
                var position = new int3(4, 5, 6);
                var first = new Block(1, 10);
                var final = new Block(2, 20);
                world.Enqueue(WorldCommand.SetBlock(position, first));
                world.Enqueue(WorldCommand.SetBlock(position, final));
                world.Enqueue(WorldCommand.SetBlock(position, final));

                var result = world.Flush();

                Assert.That(result.CommandsApplied, Is.EqualTo(3));
                Assert.That(result.ChangedCells, Is.EqualTo(1));
                Assert.That(result.DirtyChunks, Is.EqualTo(1));
                Assert.That(world.Changes.Length, Is.EqualTo(1));
                Assert.That(world.Changes[0].WorldPosition, Is.EqualTo(position));
                Assert.That(world.Changes[0].Original, Is.EqualTo(Block.Air));
                Assert.That(world.Changes[0].Final, Is.EqualTo(final));
                Assert.That(world.DirtyChunks[0].Flags, Is.EqualTo(ChunkDirtyFlags.Data));
                Assert.That(world.IsChunkVersionCurrent(
                    world.DirtyChunks[0].Handle,
                    world.DirtyChunks[0].Version), Is.True);

                world.Enqueue(WorldCommand.SetBlock(position, final));
                var noOp = world.Flush();
                Assert.That(noOp.ChangedCells, Is.Zero);
                Assert.That(noOp.DirtyChunks, Is.Zero);
            }
        }

        [Test]
        public void AirWriteToUnloadedChunk_IsNoOpAndDoesNotAllocateChunk()
        {
            using (var world = new WorldCore())
            {
                world.Enqueue(WorldCommand.SetBlock(new int3(-1, 0, 0), Block.Air));
                world.Enqueue(WorldCommand.RemoveBlock(new int3(-1, 0, 0)));
                var result = world.Flush();

                Assert.That(result.ChangedCells, Is.Zero);
                Assert.That(result.DirtyChunks, Is.Zero);
                Assert.That(world.LoadedChunkCount, Is.Zero);
                Assert.That(world.TryGetBlock(new int3(-1, 0, 0), out var block), Is.False);
                Assert.That(block, Is.EqualTo(Block.Air));
            }
        }

        [Test]
        public void BoundaryChange_InvalidatesLoadedAdjacentChunkWithoutDuplicates()
        {
            using (var world = new WorldCore())
            {
                world.Enqueue(WorldCommand.SetBlock(new int3(20, 4, 4), new Block(1)));
                world.Flush();

                world.Enqueue(WorldCommand.SetBlock(new int3(15, 4, 4), new Block(2)));
                world.Enqueue(WorldCommand.SetBlock(new int3(15, 4, 5), new Block(3)));
                world.Flush();

                Assert.That(world.DirtyChunks.Length, Is.EqualTo(2));
                var own = world.DirtyChunks.ToArray()
                    .Single(item => item.ChunkCoordinate.Equals(int3.zero));
                var neighbor = world.DirtyChunks.ToArray()
                    .Single(item => item.ChunkCoordinate.Equals(new int3(1, 0, 0)));
                Assert.That(own.Flags.HasFlag(ChunkDirtyFlags.Data), Is.True);
                Assert.That(neighbor.Flags.HasFlag(ChunkDirtyFlags.Neighbor), Is.True);
            }
        }

        [Test]
        public void LaterFlush_InvalidatesPreviouslyCapturedCompletionVersion()
        {
            using (var world = new WorldCore())
            {
                var position = new int3(3, 3, 3);
                world.Enqueue(WorldCommand.SetBlock(position, new Block(1)));
                world.Flush();
                var handle = world.DirtyChunks[0].Handle;
                var firstVersion = world.DirtyChunks[0].Version;
                Assert.That(world.IsChunkVersionCurrent(handle, firstVersion), Is.True);

                world.Enqueue(WorldCommand.SetBlock(position, new Block(2)));
                world.Flush();
                var secondVersion = world.DirtyChunks[0].Version;
                Assert.That(secondVersion, Is.GreaterThan(firstVersion));
                Assert.That(world.IsChunkVersionCurrent(handle, firstVersion), Is.False);
                Assert.That(world.IsChunkVersionCurrent(handle, secondVersion), Is.True);
            }
        }

        [Test]
        public void FillRegion_IsInclusiveAndBounded()
        {
            using (var world = new WorldCore(maxFillBlocksPerCommand: 8))
            {
                world.Enqueue(WorldCommand.FillRegion(
                    new int3(-1, 0, 0),
                    new int3(0, 1, 1),
                    new Block(7, 9)));
                var result = world.Flush();
                Assert.That(result.ChangedCells, Is.EqualTo(8));
                Assert.That(world.GetBlockOrAir(new int3(-1, 1, 1)), Is.EqualTo(new Block(7, 9)));

                Assert.Throws<ArgumentOutOfRangeException>(() => world.Enqueue(
                    WorldCommand.FillRegion(int3.zero, new int3(2, 2, 0), new Block(1))));
            }
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            var world = new WorldCore();
            world.Dispose();
            Assert.DoesNotThrow(world.Dispose);
            Assert.Throws<ObjectDisposedException>(() => world.TryGetBlock(int3.zero, out _));
        }

        [Test]
        public void NewWorldAfterDisposedInstance_ReinitializesNativeOwnership()
        {
            var disposedWorld = new WorldCore(initialChunkCapacity: 1);
            disposedWorld.Enqueue(WorldCommand.SetBlock(int3.zero, new Block(1, 7)));
            disposedWorld.Flush();
            disposedWorld.Dispose();

            using (var reinitializedWorld = new WorldCore(initialChunkCapacity: 1))
            {
                Assert.That(reinitializedWorld.LoadedChunkCount, Is.Zero);
                Assert.That(reinitializedWorld.GetBlockOrAir(int3.zero), Is.EqualTo(Block.Air));

                var expected = new Block(2, 9);
                reinitializedWorld.Enqueue(WorldCommand.SetBlock(new int3(-1, -1, -1), expected));
                reinitializedWorld.Flush();

                Assert.That(
                    reinitializedWorld.GetBlockOrAir(new int3(-1, -1, -1)),
                    Is.EqualTo(expected));
            }
        }

        [Test]
        public void RepeatedCommandApplication_AfterWarmup_HasNoManagedGcAllocation()
        {
            using (var world = new WorldCore())
            {
                var position = new int3(3, 3, 3);
                for (var i = 0; i < 32; i++)
                {
                    world.Enqueue(WorldCommand.SetBlock(position, new Block(1, (uint)(i & 1))));
                    world.Flush();
                }

                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 1_000; i++)
                {
                    world.Enqueue(WorldCommand.SetBlock(position, new Block(1, (uint)(i & 1))));
                    world.Flush();
                }
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allocated, Is.Zero,
                    "Construction, native capacity growth, and the test runner are outside this window.");
            }
        }
    }
}
