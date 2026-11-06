using System.Collections.Generic;

namespace MF2.Game.Networking
{
    public sealed class ConnectionSpawnGuard
    {
        private readonly HashSet<int> connectionIds = new();

        public int Count => connectionIds.Count;
        public bool TryReserve(int connectionId) => connectionIds.Add(connectionId);
        public bool Release(int connectionId) => connectionIds.Remove(connectionId);
        public bool Contains(int connectionId) => connectionIds.Contains(connectionId);
        public void Clear() => connectionIds.Clear();
    }
}
