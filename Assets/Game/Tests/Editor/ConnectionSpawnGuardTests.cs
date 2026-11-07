using MF2.Game.Networking;
using NUnit.Framework;

namespace MF2.Tests
{
    public sealed class ConnectionSpawnGuardTests
    {
        [Test]
        public void SameConnection_CannotBeReservedTwice()
        {
            var guard = new ConnectionSpawnGuard();

            Assert.That(guard.TryReserve(42), Is.True);
            Assert.That(guard.TryReserve(42), Is.False);
            Assert.That(guard.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReleasedConnection_CanBeReservedAgain()
        {
            var guard = new ConnectionSpawnGuard();
            guard.TryReserve(7);

            Assert.That(guard.Release(7), Is.True);
            Assert.That(guard.TryReserve(7), Is.True);
        }

        [Test]
        public void CommandGuard_RejectsReplayAndOutOfOrderSequences()
        {
            var guard = new ConnectionCommandGuard();

            Assert.That(guard.TryAccept(3, 10, 5, out var accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(ConnectionCommandRejection.None));
            Assert.That(guard.TryAccept(3, 10, 5, out var duplicate), Is.False);
            Assert.That(duplicate, Is.EqualTo(ConnectionCommandRejection.DuplicateOrOutOfOrder));
            Assert.That(guard.TryAccept(3, 9, 6, out var outOfOrder), Is.False);
            Assert.That(outOfOrder, Is.EqualTo(ConnectionCommandRejection.DuplicateOrOutOfOrder));
        }

        [Test]
        public void CommandGuard_RateLimitsPerAuthoritativeTickAndConsumesRejectedSequence()
        {
            var guard = new ConnectionCommandGuard(maxCommandsPerTick: 2);

            Assert.That(guard.TryAccept(4, 1, 20, out _), Is.True);
            Assert.That(guard.TryAccept(4, 2, 20, out _), Is.True);
            Assert.That(guard.TryAccept(4, 3, 20, out var limited), Is.False);
            Assert.That(limited, Is.EqualTo(ConnectionCommandRejection.RateLimited));
            Assert.That(guard.TryAccept(4, 3, 21, out var replay), Is.False);
            Assert.That(replay, Is.EqualTo(ConnectionCommandRejection.DuplicateOrOutOfOrder));
            Assert.That(guard.TryAccept(4, 4, 21, out _), Is.True);
        }

        [Test]
        public void CommandGuard_ReleaseAllowsFreshConnectionSequence()
        {
            var guard = new ConnectionCommandGuard();
            guard.TryAccept(9, 100, 1, out _);

            Assert.That(guard.Release(9), Is.True);
            Assert.That(guard.TryAccept(9, 1, 1, out _), Is.True);
        }
    }
}
