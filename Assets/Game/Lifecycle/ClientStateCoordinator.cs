using System;
using System.Collections.Generic;
using MF2.Game.Domain;
using MF2.World.Core;
using MF2.World.Storage;
using Unity.Mathematics;

namespace MF2.Game.Lifecycle
{
    public readonly struct ClientWorldReadiness
    {
        public ClientWorldReadiness(SessionId sessionId, long generation, long tickIndex)
        {
            SessionId = sessionId;
            Generation = generation;
            TickIndex = tickIndex;
        }

        public SessionId SessionId { get; }
        public long Generation { get; }
        public long TickIndex { get; }
    }

    public interface IClientReplicatedWorld
    {
        event Action Changed;
        bool HasSession { get; }
        SessionId SessionId { get; }
        long Generation { get; }
        long TickIndex { get; }
        Block GetBlockOrAir(int3 position);
    }

    public interface IClientWorldViewSource
    {
        event Action<WorldCore> WorldReplaced;
        event Action<WorldCore> WorldFlushed;
        event Action<WorldCore> WorldClearing;
        bool TryGetWorld(out WorldCore current);
        void MarkCollisionReady(WorldCore current);
        void MarkCollisionUnavailable(WorldCore current);
    }

    /// <summary>
    /// Client-presentation-scope replica. It exposes only value snapshots and owns a client-only
    /// WorldCore; host presentation never reads the server GameSession or PlayerRegistry.
    /// </summary>
    public sealed class ClientStateCoordinator :
        IClientReplicatedWorld,
        IClientWorldViewSource,
        IEntityStateReader,
        IDisposable
    {
        private readonly Dictionary<PlayerId, PlayerModel> players =
            new Dictionary<PlayerId, PlayerModel>();
        private WorldCore world;
        private SessionId sessionId;
        private long generation;
        private long worldTickIndex = -1;
        private long playerTickIndex = -1;
        private long collisionReadyTickIndex = -1;
        private bool hasSession;
        private bool disposed;

        public event Action Changed;
        public event Action<PlayerStateChange> PlayerStateChanged;
        public event Action<WorldCore> WorldReplaced;
        public event Action<WorldCore> WorldFlushed;
        public event Action<WorldCore> WorldClearing;
        public event Action<ClientWorldReadiness> WorldCollisionReady;

        public bool HasSession => hasSession;
        public SessionId SessionId => sessionId;
        public long Generation => generation;
        public long TickIndex => Math.Max(worldTickIndex, playerTickIndex);
        public bool IsWorldCollisionReady =>
            hasSession && world != null && collisionReadyTickIndex == worldTickIndex;

        public Block GetBlockOrAir(int3 position)
        {
            ThrowIfDisposed();
            return world == null ? Block.Air : world.GetBlockOrAir(position);
        }

        public bool TryGetPlayer(PlayerId playerId, out PlayerModel snapshot)
        {
            ThrowIfDisposed();
            return players.TryGetValue(playerId, out snapshot);
        }

        public bool ApplySnapshot(
            SessionId id,
            long sourceGeneration,
            long sourceTickIndex,
            WorldStorageSnapshot snapshot)
        {
            if (sourceTickIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTickIndex));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            ThrowIfDisposed();
            if (!PrepareSnapshotSession(id, sourceGeneration))
                return false;
            if (world != null && sourceTickIndex < worldTickIndex)
                return false;

            var replacement = WorldStorageWorldBuilder.CreateWorld(snapshot);
            var previous = world;
            world = replacement;
            worldTickIndex = sourceTickIndex;
            collisionReadyTickIndex = -1;
            WorldReplaced?.Invoke(replacement);
            ReleaseWorld(previous);
            Changed?.Invoke();
            return true;
        }

        public bool ApplyDelta(
            SessionId id,
            long sourceGeneration,
            long sourceTickIndex,
            IReadOnlyList<WorldBlockDelta> changes)
        {
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            ThrowIfDisposed();
            if (!hasSession || world == null || sessionId != id || generation != sourceGeneration ||
                sourceTickIndex <= worldTickIndex)
                return false;

            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                world.Enqueue(change.Final.IsAir
                    ? WorldCommand.RemoveBlock(change.Position)
                    : WorldCommand.SetBlock(change.Position, change.Final));
            }
            world.Flush();
            worldTickIndex = sourceTickIndex;
            collisionReadyTickIndex = -1;
            WorldFlushed?.Invoke(world);
            Changed?.Invoke();
            return true;
        }

        public bool ApplyPlayerSnapshot(
            SessionId id,
            long sourceGeneration,
            long sourceTickIndex,
            IReadOnlyList<PlayerModel> snapshots)
        {
            if (sourceTickIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTickIndex));
            if (snapshots == null)
                throw new ArgumentNullException(nameof(snapshots));
            ThrowIfDisposed();
            if (!PrepareSnapshotSession(id, sourceGeneration))
                return false;
            if (sourceTickIndex < playerTickIndex)
                return false;

            var replacement = new Dictionary<PlayerId, PlayerModel>(snapshots.Count);
            for (var i = 0; i < snapshots.Count; i++)
            {
                var snapshot = snapshots[i];
                if (snapshot.Id.Value == Guid.Empty || !replacement.TryAdd(snapshot.Id, snapshot))
                    throw new ArgumentException("Player snapshots must have unique, valid ids.", nameof(snapshots));
            }

            if (players.Count > 0)
            {
                var previous = new PlayerModel[players.Count];
                players.Values.CopyTo(previous, 0);
                for (var i = 0; i < previous.Length; i++)
                {
                    var snapshot = previous[i];
                    if (!replacement.ContainsKey(snapshot.Id))
                    {
                        PlayerStateChanged?.Invoke(PlayerStateChange.Removed(
                            snapshot.Id,
                            checked(snapshot.Revision + 1)));
                    }
                }
            }

            players.Clear();
            foreach (var pair in replacement)
            {
                players.Add(pair.Key, pair.Value);
                PlayerStateChanged?.Invoke(PlayerStateChange.Upserted(pair.Value));
            }
            playerTickIndex = sourceTickIndex;
            Changed?.Invoke();
            return true;
        }

        public bool ApplyPlayerDelta(
            SessionId id,
            long sourceGeneration,
            long sourceTickIndex,
            IReadOnlyList<PlayerStateChange> changes)
        {
            if (sourceTickIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceTickIndex));
            if (changes == null)
                throw new ArgumentNullException(nameof(changes));
            ThrowIfDisposed();
            // A delta is never allowed to establish or replace a replica. The matching initial
            // player snapshot is the ordering barrier for reconnect and late join.
            if (!hasSession || sessionId != id || generation != sourceGeneration ||
                playerTickIndex < 0 ||
                sourceTickIndex <= playerTickIndex)
            {
                return false;
            }

            for (var i = 0; i < changes.Count; i++)
                ApplyPlayerChange(changes[i]);
            playerTickIndex = sourceTickIndex;
            Changed?.Invoke();
            return true;
        }

        public void ClearPlayers()
        {
            if (disposed || players.Count == 0)
                return;

            PublishPlayerRemovals();
            players.Clear();
            playerTickIndex = -1;
            Changed?.Invoke();
        }

        public void Clear()
        {
            if (disposed || (!hasSession && world == null && players.Count == 0))
                return;

            ResetState();
            Changed?.Invoke();
        }

        public bool TryGetWorld(out WorldCore current)
        {
            current = world;
            return current != null;
        }

        public void MarkCollisionReady(WorldCore current)
        {
            ThrowIfDisposed();
            if (current == null || !ReferenceEquals(world, current) || !hasSession || worldTickIndex < 0)
                return;
            if (collisionReadyTickIndex == worldTickIndex)
                return;

            collisionReadyTickIndex = worldTickIndex;
            WorldCollisionReady?.Invoke(
                new ClientWorldReadiness(sessionId, generation, collisionReadyTickIndex));
        }

        public void MarkCollisionUnavailable(WorldCore current)
        {
            if (disposed || current == null || !ReferenceEquals(world, current))
                return;
            collisionReadyTickIndex = -1;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            Clear();
            Changed = null;
            PlayerStateChanged = null;
            WorldReplaced = null;
            WorldFlushed = null;
            WorldClearing = null;
            WorldCollisionReady = null;
            disposed = true;
        }

        private bool PrepareSnapshotSession(SessionId id, long sourceGeneration)
        {
            if (id.Value == Guid.Empty)
                throw new ArgumentException("A client replica requires a valid session id.", nameof(id));
            if (sourceGeneration <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceGeneration));
            if (hasSession && sessionId == id && generation == sourceGeneration)
                return true;
            if (hasSession && sourceGeneration <= generation)
                return false;

            if (hasSession)
                ResetState();
            sessionId = id;
            generation = sourceGeneration;
            hasSession = true;
            return true;
        }

        private void ApplyPlayerChange(in PlayerStateChange change)
        {
            if (change.PlayerId.Value == Guid.Empty)
                return;

            if (change.Kind == PlayerStateChangeKind.Removed)
            {
                if (players.TryGetValue(change.PlayerId, out var existing) &&
                    change.Revision > existing.Revision)
                {
                    players.Remove(change.PlayerId);
                    PlayerStateChanged?.Invoke(change);
                }
                return;
            }

            var snapshot = change.Snapshot;
            if (snapshot.Id != change.PlayerId || snapshot.Revision != change.Revision)
                return;
            if (players.TryGetValue(snapshot.Id, out var current) &&
                snapshot.Revision <= current.Revision)
            {
                return;
            }

            players[snapshot.Id] = snapshot;
            PlayerStateChanged?.Invoke(PlayerStateChange.Upserted(snapshot));
        }

        private void ResetState()
        {
            var released = world;
            world = null;
            PublishPlayerRemovals();
            players.Clear();
            sessionId = default;
            generation = 0;
            worldTickIndex = -1;
            playerTickIndex = -1;
            collisionReadyTickIndex = -1;
            hasSession = false;
            ReleaseWorld(released);
        }

        private void PublishPlayerRemovals()
        {
            if (players.Count == 0 || PlayerStateChanged == null)
                return;

            foreach (var snapshot in players.Values)
            {
                PlayerStateChanged.Invoke(PlayerStateChange.Removed(
                    snapshot.Id,
                    checked(snapshot.Revision + 1)));
            }
        }

        private void ReleaseWorld(WorldCore released)
        {
            if (released == null)
                return;
            WorldClearing?.Invoke(released);
            released.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ClientStateCoordinator));
        }
    }
}
