using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using MF2.World.Storage;
using Unity.Mathematics;
using UnityEngine;
using VContainer;

namespace MF2.Game.Networking
{
    public struct ReplicatedBlock
    {
        public int X;
        public int Y;
        public int Z;
        public uint Id;
        public uint State;
    }

    public struct WorldSnapshotBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public ReplicatedBlock[] Blocks;
    }

    public struct WorldDeltaBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public ReplicatedBlock[] Blocks;
    }

    public struct WorldReadyBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
    }

    /// <summary>
    /// FishNet boundary for managed snapshots and deltas. Server WorldCore/native views never
    /// cross this component; clients apply messages to a separate read-side WorldCore.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(ServerSessionCoordinator))]
    public sealed class WorldReplicationAdapter : MonoBehaviour
    {
        private readonly Dictionary<int, ClientWorldReadiness> readyClients = new();
        private NetworkManager networkManager;
        private ServerSessionCoordinator coordinator;
        private ClientPresentationCoordinator clientPresentation;
        private ClientStateCoordinator clientState;
        private GameSession subscribedSession;
        private bool networkCallbacksRegistered;
        private long requiredWorldReadyTick = -1;

        public event Action<NetworkConnection> ConnectionWorldReady;

        [Inject]
        private void Construct(ClientPresentationCoordinator presentation)
        {
            clientPresentation = presentation;
        }

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            coordinator = GetComponent<ServerSessionCoordinator>();
        }

        private void OnEnable()
        {
            coordinator ??= GetComponent<ServerSessionCoordinator>();
            coordinator.SessionStarted += OnSessionStarted;
            coordinator.SessionStopping += OnSessionStopping;
        }

        private void Start()
        {
            clientPresentation.PresentationReady += OnPresentationReady;
            clientPresentation.PresentationStopping += OnPresentationStopping;
            if (clientPresentation.TryGetState(out var readyState))
                OnPresentationReady(readyState);

            networkManager.ClientManager.RegisterBroadcast<WorldSnapshotBroadcast>(OnSnapshotReceived);
            networkManager.ClientManager.RegisterBroadcast<WorldDeltaBroadcast>(OnDeltaReceived);
            networkManager.ServerManager.RegisterBroadcast<WorldReadyBroadcast>(
                OnWorldReadyReceived,
                requireAuthentication: true);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ServerManager.OnAuthenticationResult += OnServerAuthenticationResult;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            networkCallbacksRegistered = true;

            if (coordinator.TryGetSession(out var activeSession))
                OnSessionStarted(activeSession);
        }

        private void OnSessionStarted(GameSession startedSession)
        {
            if (ReferenceEquals(subscribedSession, startedSession))
                return;
            if (subscribedSession != null)
                subscribedSession.TickCompleted -= OnSessionTickCompleted;
            subscribedSession = startedSession;
            subscribedSession.TickCompleted += OnSessionTickCompleted;
            readyClients.Clear();
            requiredWorldReadyTick = startedSession.TickIndex;
            if (networkManager != null && networkManager.IsServerStarted)
                networkManager.ServerManager.Broadcast(CreateSnapshot(startedSession.CaptureSnapshot()));
        }

        private void OnSessionStopping(GameSession endingSession)
        {
            if (!ReferenceEquals(subscribedSession, endingSession))
                return;
            subscribedSession.TickCompleted -= OnSessionTickCompleted;
            subscribedSession = null;
            readyClients.Clear();
            requiredWorldReadyTick = -1;
        }

        private void OnServerAuthenticationResult(NetworkConnection connection, bool authenticated)
        {
            if (!authenticated || !coordinator.TryGetSession(out var activeSession))
            {
                if (!authenticated)
                    readyClients.Remove(connection.ClientId);
                return;
            }
            networkManager.ServerManager.Broadcast(connection, CreateSnapshot(activeSession.CaptureSnapshot()));
        }

        private void OnSessionTickCompleted(SessionTickResult result)
        {
            if (result.Delta.BlockChanges.Count == 0 || subscribedSession == null)
                return;

            requiredWorldReadyTick = result.TickIndex;

            var blocks = new ReplicatedBlock[result.Delta.BlockChanges.Count];
            for (var i = 0; i < blocks.Length; i++)
            {
                var change = result.Delta.BlockChanges[i];
                blocks[i] = ToReplicatedBlock(change.Position, change.Final);
            }

            networkManager.ServerManager.Broadcast(new WorldDeltaBroadcast
            {
                SessionId = subscribedSession.Id.Value,
                Generation = subscribedSession.Generation,
                TickIndex = result.TickIndex,
                Blocks = blocks
            });
        }

        private void OnSnapshotReceived(WorldSnapshotBroadcast message, Channel channel)
        {
            if (clientState == null || message.SessionId == Guid.Empty || message.Generation <= 0)
                return;

            var chunks = new Dictionary<int3, List<StoredBlock>>();
            var blocks = message.Blocks ?? Array.Empty<ReplicatedBlock>();
            for (var i = 0; i < blocks.Length; i++)
            {
                var source = blocks[i];
                var position = new int3(source.X, source.Y, source.Z);
                ChunkCoordinates.Split(position, out var chunk, out var local);
                if (!chunks.TryGetValue(chunk, out var storedBlocks))
                {
                    storedBlocks = new List<StoredBlock>();
                    chunks.Add(chunk, storedBlocks);
                }
                storedBlocks.Add(new StoredBlock(
                    ChunkCoordinates.ToIndex(local),
                    source.Id,
                    source.State));
            }

            var storedChunks = new List<StoredChunk>(chunks.Count);
            foreach (var pair in chunks)
                storedChunks.Add(new StoredChunk(pair.Key, pair.Value));
            clientState.ApplySnapshot(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex,
                new WorldStorageSnapshot(storedChunks));
        }

        private void OnDeltaReceived(WorldDeltaBroadcast message, Channel channel)
        {
            if (clientState == null || message.SessionId == Guid.Empty || message.Generation <= 0)
                return;
            var blocks = message.Blocks ?? Array.Empty<ReplicatedBlock>();
            var changes = new WorldBlockDelta[blocks.Length];
            for (var i = 0; i < blocks.Length; i++)
            {
                var source = blocks[i];
                var block = source.Id == 0 ? Block.Air : new Block(source.Id, source.State);
                changes[i] = new WorldBlockDelta(
                    new int3(source.X, source.Y, source.Z),
                    Block.Air,
                    block);
            }
            clientState.ApplyDelta(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex,
                changes);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
                clientState?.Clear();
        }

        public bool IsConnectionWorldReady(NetworkConnection connection, GameSession session)
        {
            if (connection == null || session == null || !ReferenceEquals(session, subscribedSession) ||
                !readyClients.TryGetValue(connection.ClientId, out var ready))
            {
                return false;
            }

            return ready.SessionId == session.Id && ready.Generation == session.Generation &&
                   ready.TickIndex >= requiredWorldReadyTick;
        }

        public void InvalidateConnectionWorldReady(NetworkConnection connection)
        {
            if (connection != null)
                readyClients.Remove(connection.ClientId);
        }

        private void OnRemoteConnectionState(
            NetworkConnection connection,
            RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped)
                InvalidateConnectionWorldReady(connection);
        }

        private void OnWorldReadyReceived(
            NetworkConnection connection,
            WorldReadyBroadcast message,
            Channel channel)
        {
            if (channel != Channel.Reliable || message.SessionId == Guid.Empty ||
                !coordinator.TryGetSession(out var activeSession) ||
                message.SessionId != activeSession.Id.Value ||
                message.Generation != activeSession.Generation ||
                message.TickIndex < requiredWorldReadyTick ||
                message.TickIndex > activeSession.TickIndex)
            {
                return;
            }

            var ready = new ClientWorldReadiness(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex);
            if (readyClients.TryGetValue(connection.ClientId, out var previous) &&
                previous.SessionId == ready.SessionId && previous.Generation == ready.Generation &&
                previous.TickIndex >= ready.TickIndex)
            {
                return;
            }

            readyClients[connection.ClientId] = ready;
            ConnectionWorldReady?.Invoke(connection);
        }

        private void OnPresentationReady(ClientStateCoordinator state)
        {
            if (clientState != null)
                clientState.WorldCollisionReady -= OnClientWorldCollisionReady;
            clientState = state;
            clientState.WorldCollisionReady += OnClientWorldCollisionReady;
        }

        private void OnPresentationStopping(ClientStateCoordinator state)
        {
            if (ReferenceEquals(clientState, state))
            {
                clientState.WorldCollisionReady -= OnClientWorldCollisionReady;
                clientState = null;
            }
        }

        private void OnClientWorldCollisionReady(ClientWorldReadiness ready)
        {
            if (networkManager == null || !networkManager.IsClientStarted)
                return;
            networkManager.ClientManager.Broadcast(
                new WorldReadyBroadcast
                {
                    SessionId = ready.SessionId.Value,
                    Generation = ready.Generation,
                    TickIndex = ready.TickIndex
                },
                Channel.Reliable);
        }

        private static WorldSnapshotBroadcast CreateSnapshot(SessionSnapshot snapshot)
        {
            var blocks = new List<ReplicatedBlock>();
            for (var chunkIndex = 0; chunkIndex < snapshot.World.Chunks.Count; chunkIndex++)
            {
                var chunk = snapshot.World.Chunks[chunkIndex];
                for (var blockIndex = 0; blockIndex < chunk.Blocks.Count; blockIndex++)
                {
                    var stored = chunk.Blocks[blockIndex];
                    var local = ChunkCoordinates.FromIndex(stored.Index);
                    var position = new int3(
                        checked(chunk.ChunkCoordinate.x * ChunkCoordinates.Size + local.x),
                        checked(chunk.ChunkCoordinate.y * ChunkCoordinates.Size + local.y),
                        checked(chunk.ChunkCoordinate.z * ChunkCoordinates.Size + local.z));
                    blocks.Add(ToReplicatedBlock(position, stored.ToBlock()));
                }
            }

            return new WorldSnapshotBroadcast
            {
                SessionId = snapshot.Id.Value,
                Generation = snapshot.Generation,
                TickIndex = snapshot.TickIndex,
                Blocks = blocks.ToArray()
            };
        }

        private static ReplicatedBlock ToReplicatedBlock(int3 position, Block block)
        {
            return new ReplicatedBlock
            {
                X = position.x,
                Y = position.y,
                Z = position.z,
                Id = block.Id,
                State = block.State
            };
        }

        private void OnDestroy()
        {
            if (coordinator != null)
            {
                coordinator.SessionStarted -= OnSessionStarted;
                coordinator.SessionStopping -= OnSessionStopping;
            }
            if (subscribedSession != null)
                subscribedSession.TickCompleted -= OnSessionTickCompleted;
            if (clientPresentation != null)
            {
                clientPresentation.PresentationReady -= OnPresentationReady;
                clientPresentation.PresentationStopping -= OnPresentationStopping;
            }
            if (clientState != null)
                clientState.WorldCollisionReady -= OnClientWorldCollisionReady;
            if (!networkCallbacksRegistered || networkManager == null)
                return;

            var clientManager = networkManager.ClientManager;
            if (clientManager != null)
            {
                clientManager.UnregisterBroadcast<WorldSnapshotBroadcast>(OnSnapshotReceived);
                clientManager.UnregisterBroadcast<WorldDeltaBroadcast>(OnDeltaReceived);
                clientManager.OnClientConnectionState -= OnClientConnectionState;
            }

            if (networkManager.ServerManager != null)
            {
                networkManager.ServerManager.UnregisterBroadcast<WorldReadyBroadcast>(OnWorldReadyReceived);
                networkManager.ServerManager.OnAuthenticationResult -= OnServerAuthenticationResult;
                networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
            ConnectionWorldReady = null;
            readyClients.Clear();
            networkCallbacksRegistered = false;
        }
    }
}
