using System;
using System.Collections.Generic;
using FishNet.Object;
using MF2.Game.Domain;

namespace MF2.Game.Physics
{
    public interface IPlayerNetworkObjectMap
    {
        int Count { get; }
        bool TryRegister(PlayerId playerId, NetworkObject networkObject);
        bool Unregister(PlayerId playerId, NetworkObject expectedObject = null);
        bool TryGetNetworkObject(PlayerId playerId, out NetworkObject networkObject);
        bool TryGetPlayerId(NetworkObject networkObject, out PlayerId playerId);
    }

    /// <summary>
    /// Server-session map between stable domain identity and the current FishNet instance. A new
    /// object may register the same PlayerId only after the previous object was unregistered.
    /// </summary>
    public sealed class PlayerNetworkObjectMap : IPlayerNetworkObjectMap, IDisposable
    {
        private readonly Dictionary<PlayerId, NetworkObject> objectsByPlayer =
            new Dictionary<PlayerId, NetworkObject>();
        private readonly Dictionary<NetworkObject, PlayerId> playersByObject =
            new Dictionary<NetworkObject, PlayerId>();
        private bool disposed;

        public int Count => objectsByPlayer.Count;

        public bool TryRegister(PlayerId playerId, NetworkObject networkObject)
        {
            ThrowIfDisposed();
            if (playerId.Value == Guid.Empty || networkObject == null)
                return false;
            if (objectsByPlayer.TryGetValue(playerId, out var existingObject))
                return ReferenceEquals(existingObject, networkObject);
            if (playersByObject.TryGetValue(networkObject, out var existingPlayer))
                return existingPlayer == playerId;

            objectsByPlayer.Add(playerId, networkObject);
            playersByObject.Add(networkObject, playerId);
            return true;
        }

        public bool Unregister(PlayerId playerId, NetworkObject expectedObject = null)
        {
            if (disposed || !objectsByPlayer.TryGetValue(playerId, out var networkObject))
                return false;
            if (expectedObject != null && !ReferenceEquals(networkObject, expectedObject))
                return false;

            objectsByPlayer.Remove(playerId);
            playersByObject.Remove(networkObject);
            return true;
        }

        public bool TryGetNetworkObject(PlayerId playerId, out NetworkObject networkObject)
        {
            ThrowIfDisposed();
            if (!objectsByPlayer.TryGetValue(playerId, out networkObject))
                return false;
            if (networkObject != null)
                return true;
            Unregister(playerId);
            networkObject = null;
            return false;
        }

        public bool TryGetPlayerId(NetworkObject networkObject, out PlayerId playerId)
        {
            ThrowIfDisposed();
            if (networkObject != null && playersByObject.TryGetValue(networkObject, out playerId))
                return true;
            playerId = default;
            return false;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            objectsByPlayer.Clear();
            playersByObject.Clear();
            disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(PlayerNetworkObjectMap));
        }
    }
}
