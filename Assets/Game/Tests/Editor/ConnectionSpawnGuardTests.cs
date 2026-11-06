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
    }
}
