using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.World.Core;
using Unity.Mathematics;
using UnityEngine;

namespace MF2.Game.Networking
{
    public interface IGameCommandSender
    {
        bool SendWorldCommand(WorldCommand command);
    }

    public struct WorldCommandBroadcast : IBroadcast
    {
        public ulong Sequence;
        public byte Type;
        public int MinX;
        public int MinY;
        public int MinZ;
        public int MaxX;
        public int MaxY;
        public int MaxZ;
        public uint BlockId;
        public uint BlockState;

        public static WorldCommandBroadcast FromCommand(WorldCommand command, ulong sequence)
        {
            if (command.Type == WorldCommandType.None)
                throw new System.ArgumentException("An empty world command cannot be sent.", nameof(command));
            if (sequence == 0)
                throw new System.ArgumentOutOfRangeException(nameof(sequence));

            return new WorldCommandBroadcast
            {
                Sequence = sequence,
                Type = (byte)command.Type,
                MinX = command.Min.x,
                MinY = command.Min.y,
                MinZ = command.Min.z,
                MaxX = command.Max.x,
                MaxY = command.Max.y,
                MaxZ = command.Max.z,
                BlockId = command.Block.Id,
                BlockState = command.Block.State
            };
        }

        public bool TryToCommand(out WorldCommand command)
        {
            command = default;
            if (Sequence == 0 || (BlockId == 0 && BlockState != 0))
                return false;

            var min = new int3(MinX, MinY, MinZ);
            var max = new int3(MaxX, MaxY, MaxZ);
            var block = new Block(BlockId, BlockState);
            switch ((WorldCommandType)Type)
            {
                case WorldCommandType.SetBlock when !block.IsAir && min.Equals(max):
                    command = WorldCommand.SetBlock(min, block);
                    return true;
                case WorldCommandType.RemoveBlock when block.IsAir && min.Equals(max):
                    command = WorldCommand.RemoveBlock(min);
                    return true;
                case WorldCommandType.FillRegion:
                    command = WorldCommand.FillRegion(min, max, block);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Application-lifetime FishNet boundary. Clients emit identity-free command DTOs; the server
    /// maps FishNet connection identifiers to session-scoped domain player identifiers.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(ServerSessionCoordinator))]
    public sealed class GameCommandNetworkAdapter : MonoBehaviour, IGameCommandSender
    {
        private readonly Dictionary<int, PlayerId> playerIdsByConnection =
            new Dictionary<int, PlayerId>();
        private readonly ConnectionCommandGuard commandGuard = new ConnectionCommandGuard();
        private ServerSessionCoordinator coordinator;
        private NetworkManager networkManager;
        private bool networkCallbackRegistered;
        private ulong clientSequence;

        public int MappedConnectionCount => playerIdsByConnection.Count;

        private void Awake()
        {
            coordinator = GetComponent<ServerSessionCoordinator>();
            networkManager = GetComponent<NetworkManager>();
        }

        private void OnEnable()
        {
            if (coordinator == null)
                coordinator = GetComponent<ServerSessionCoordinator>();
            coordinator.SessionStopping += OnSessionStopping;
        }

        private void Start()
        {
            networkManager.ServerManager.RegisterBroadcast<WorldCommandBroadcast>(
                OnWorldCommandReceived,
                requireAuthentication: true);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkCallbackRegistered = true;
        }

        public bool SendWorldCommand(WorldCommand command)
        {
            if (networkManager == null || !networkManager.IsClientStarted ||
                command.Type == WorldCommandType.None)
            {
                return false;
            }

            if (clientSequence == ulong.MaxValue)
                return false;

            clientSequence++;
            networkManager.ClientManager.Broadcast(
                WorldCommandBroadcast.FromCommand(command, clientSequence),
                Channel.Reliable);
            return true;
        }

        public bool TryRegisterConnection(int connectionId, out PlayerId playerId)
        {
            if (playerIdsByConnection.TryGetValue(connectionId, out playerId))
                return true;
            coordinator ??= GetComponent<ServerSessionCoordinator>();
            if (connectionId < 0 || coordinator == null ||
                !coordinator.TryGetSession(out var session))
            {
                playerId = default;
                return false;
            }

            playerId = new PlayerId(System.Guid.NewGuid());
            if (!session.TryRegisterPlayer(playerId))
                return false;

            playerIdsByConnection.Add(connectionId, playerId);
            return true;
        }

        /// <summary>
        /// Rebinds a live transport connection to a player restored in the active session.
        /// The restored registry remains the owner of the player model; this method only rebuilds
        /// the transient FishNet connection map.
        /// </summary>
        public bool TryRestoreConnection(
            int connectionId,
            PlayerId restoredPlayerId,
            out PlayerId playerId)
        {
            coordinator ??= GetComponent<ServerSessionCoordinator>();
            if (playerIdsByConnection.TryGetValue(connectionId, out playerId))
            {
                return playerId == restoredPlayerId && coordinator != null &&
                       coordinator.TryGetSession(out var mappedSession) &&
                       mappedSession.Players.Contains(restoredPlayerId);
            }
            if (connectionId < 0 || restoredPlayerId.Value == System.Guid.Empty ||
                coordinator == null || !coordinator.TryGetSession(out var session) ||
                !session.Players.Contains(restoredPlayerId) ||
                playerIdsByConnection.ContainsValue(restoredPlayerId))
            {
                playerId = default;
                return false;
            }

            playerIdsByConnection.Add(connectionId, restoredPlayerId);
            playerId = restoredPlayerId;
            return true;
        }

        /// <summary>
        /// Releases only transient connection state. Used when a restored player model must stay
        /// in the candidate session after a Unity spawn failure.
        /// </summary>
        public bool ReleaseConnectionMapping(int connectionId)
        {
            commandGuard.Release(connectionId);
            return playerIdsByConnection.Remove(connectionId);
        }

        public bool UnregisterConnection(int connectionId)
        {
            commandGuard.Release(connectionId);
            if (!playerIdsByConnection.Remove(connectionId, out var playerId))
                return false;
            if (coordinator.TryGetSession(out var session))
                session.UnregisterPlayer(playerId);
            return true;
        }

        public bool TryGetPlayerId(int connectionId, out PlayerId playerId) =>
            playerIdsByConnection.TryGetValue(connectionId, out playerId);

        public bool TryEnqueueWorldCommand(
            int connectionId,
            ulong sequence,
            WorldCommand command)
        {
            if (!TryAcceptEnvelope(connectionId, sequence, out var playerId, out var session) ||
                command.Type == WorldCommandType.None)
            {
                return false;
            }

            return session.SubmitCommand(playerId, GameCommand.ApplyWorld(command)).IsAccepted;
        }

        private void OnWorldCommandReceived(
            NetworkConnection connection,
            WorldCommandBroadcast message,
            Channel channel)
        {
            // FishNet 4.7.3 authenticates before this handler because registration uses
            // requireAuthentication=true. Reliable delivery plus a monotonic sequence gives a
            // small, explicit command envelope without trusting any client identity field.
            if (channel != Channel.Reliable ||
                !TryAcceptEnvelope(
                    connection.ClientId,
                    message.Sequence,
                    out var playerId,
                    out var session) ||
                !message.TryToCommand(out var command))
            {
                return;
            }

            session.SubmitCommand(playerId, GameCommand.ApplyWorld(command));
        }

        private bool TryAcceptEnvelope(
            int connectionId,
            ulong sequence,
            out PlayerId playerId,
            out GameSession session)
        {
            session = null;
            if (!playerIdsByConnection.TryGetValue(connectionId, out playerId) ||
                !coordinator.TryGetSession(out session))
            {
                return false;
            }

            return commandGuard.TryAccept(
                connectionId,
                sequence,
                session.TickIndex,
                out _);
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Stopped)
                clientSequence = 0;
        }

        private void OnSessionStopping(GameSession endingSession)
        {
            playerIdsByConnection.Clear();
            commandGuard.Clear();
            clientSequence = 0;
        }

        private void OnDisable()
        {
            if (coordinator != null)
                coordinator.SessionStopping -= OnSessionStopping;
            playerIdsByConnection.Clear();
            commandGuard.Clear();
        }

        private void OnDestroy()
        {
            if (networkCallbackRegistered &&
                networkManager != null &&
                networkManager.ServerManager != null)
            {
                networkManager.ServerManager.UnregisterBroadcast<WorldCommandBroadcast>(
                    OnWorldCommandReceived);
                networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            }
            networkCallbackRegistered = false;
        }
    }
}
