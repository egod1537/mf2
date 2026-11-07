using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using MF2.Entities.Player;
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

    public interface IInventoryCommandSender
    {
        bool SendInventoryCommand(InventoryCommand command);
    }

    public struct PlayerActionBroadcast : IBroadcast
    {
        public ulong Sequence;
        public byte Kind;
        public float DirectionX;
        public float DirectionZ;

        public static PlayerActionBroadcast Dash(ulong sequence) => new PlayerActionBroadcast
        {
            Sequence = sequence,
            Kind = (byte)PlayerActionCommandKind.Dash
        };

        public static PlayerActionBroadcast Attack(
            ulong sequence,
            float directionX,
            float directionZ) => new PlayerActionBroadcast
        {
            Sequence = sequence,
            Kind = (byte)PlayerActionCommandKind.Attack,
            DirectionX = directionX,
            DirectionZ = directionZ
        };

        public bool TryGetDirection(out Vector3 direction)
        {
            direction = new Vector3(DirectionX, 0f, DirectionZ);
            return Sequence > 0 &&
                   !float.IsNaN(DirectionX) && !float.IsInfinity(DirectionX) &&
                   !float.IsNaN(DirectionZ) && !float.IsInfinity(DirectionZ) &&
                   direction.sqrMagnitude >= 0.25f && direction.sqrMagnitude <= 2.25f;
        }
    }

    public struct InventoryCommandBroadcast : IBroadcast
    {
        public ulong Sequence;
        public byte Kind;
        public byte SourceContainer;
        public int SourceSlot;
        public byte TargetContainer;
        public int TargetSlot;
        public int Count;
        public int SelectedQuickbarSlot;

        public static InventoryCommandBroadcast FromCommand(
            InventoryCommand command,
            ulong sequence)
        {
            if (!command.IsStructurallyValid)
                throw new System.ArgumentException("Invalid inventory command.", nameof(command));
            if (sequence == 0)
                throw new System.ArgumentOutOfRangeException(nameof(sequence));
            return new InventoryCommandBroadcast
            {
                Sequence = sequence,
                Kind = (byte)command.Kind,
                SourceContainer = (byte)command.SourceContainer,
                SourceSlot = command.SourceSlot,
                TargetContainer = (byte)command.TargetContainer,
                TargetSlot = command.TargetSlot,
                Count = command.Count,
                SelectedQuickbarSlot = command.SelectedQuickbarSlot
            };
        }

        public bool TryToCommand(out InventoryCommand command)
        {
            command = (InventoryCommandKind)Kind switch
            {
                InventoryCommandKind.SelectQuickbar =>
                    InventoryCommand.SelectQuickbar(SelectedQuickbarSlot),
                InventoryCommandKind.Move => InventoryCommand.Move(
                    (InventoryContainerKind)SourceContainer,
                    SourceSlot,
                    (InventoryContainerKind)TargetContainer,
                    TargetSlot,
                    Count),
                _ => default
            };
            return Sequence > 0 && command.IsStructurallyValid;
        }
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
    public sealed class GameCommandNetworkAdapter : MonoBehaviour,
        IGameCommandSender,
        IInventoryCommandSender,
        IPlayerActionCommandSender
    {
        private readonly Dictionary<int, PlayerId> playerIdsByConnection =
            new Dictionary<int, PlayerId>();
        private readonly ConnectionCommandGuard commandGuard = new ConnectionCommandGuard();
        private ServerSessionCoordinator coordinator;
        private NetworkManager networkManager;
        private bool networkCallbackRegistered;
        private ulong clientSequence;

        public int MappedConnectionCount => playerIdsByConnection.Count;
        public event System.Action<int, PlayerId> ConnectionMapped;
        public event System.Action<int, PlayerId> ConnectionUnmapped;

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
            networkManager.ServerManager.RegisterBroadcast<InventoryCommandBroadcast>(
                OnInventoryCommandReceived,
                requireAuthentication: true);
            networkManager.ServerManager.RegisterBroadcast<PlayerActionBroadcast>(
                OnPlayerActionReceived,
                requireAuthentication: true);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            PlayerActionCommandBridge.Sender = this;
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

        public bool SendInventoryCommand(InventoryCommand command)
        {
            if (networkManager == null || !networkManager.IsClientStarted ||
                !command.IsStructurallyValid || clientSequence == ulong.MaxValue)
            {
                return false;
            }

            clientSequence++;
            networkManager.ClientManager.Broadcast(
                InventoryCommandBroadcast.FromCommand(command, clientSequence),
                Channel.Reliable);
            return true;
        }

        public bool SendDash()
        {
            if (!TryBeginClientCommand(out var sequence))
                return false;
            networkManager.ClientManager.Broadcast(
                PlayerActionBroadcast.Dash(sequence),
                Channel.Reliable);
            return true;
        }

        public bool SendAttack(float directionX, float directionZ)
        {
            var direction = new Vector2(directionX, directionZ);
            if (direction.sqrMagnitude < 0.25f || direction.sqrMagnitude > 2.25f ||
                float.IsNaN(directionX) || float.IsInfinity(directionX) ||
                float.IsNaN(directionZ) || float.IsInfinity(directionZ) ||
                !TryBeginClientCommand(out var sequence))
            {
                return false;
            }
            direction.Normalize();
            networkManager.ClientManager.Broadcast(
                PlayerActionBroadcast.Attack(sequence, direction.x, direction.y),
                Channel.Reliable);
            return true;
        }

        private bool TryBeginClientCommand(out ulong sequence)
        {
            sequence = 0;
            if (networkManager == null || !networkManager.IsClientStarted ||
                clientSequence == ulong.MaxValue)
            {
                return false;
            }
            sequence = ++clientSequence;
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
            ConnectionMapped?.Invoke(connectionId, playerId);
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
            ConnectionMapped?.Invoke(connectionId, playerId);
            return true;
        }

        /// <summary>
        /// Releases only transient connection state. Used when a restored player model must stay
        /// in the candidate session after a Unity spawn failure.
        /// </summary>
        public bool ReleaseConnectionMapping(int connectionId)
        {
            commandGuard.Release(connectionId);
            if (!playerIdsByConnection.Remove(connectionId, out var playerId))
                return false;
            ConnectionUnmapped?.Invoke(connectionId, playerId);
            return true;
        }

        public bool UnregisterConnection(int connectionId)
        {
            commandGuard.Release(connectionId);
            if (!playerIdsByConnection.Remove(connectionId, out var playerId))
                return false;
            ConnectionUnmapped?.Invoke(connectionId, playerId);
            if (coordinator.TryGetSession(out var session))
                session.UnregisterPlayer(playerId);
            return true;
        }

        public bool TryGetPlayerId(int connectionId, out PlayerId playerId) =>
            playerIdsByConnection.TryGetValue(connectionId, out playerId);

        public bool TryGetConnectionId(PlayerId playerId, out int connectionId)
        {
            foreach (var pair in playerIdsByConnection)
            {
                if (pair.Value != playerId)
                    continue;
                connectionId = pair.Key;
                return true;
            }
            connectionId = -1;
            return false;
        }

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

        private void OnInventoryCommandReceived(
            NetworkConnection connection,
            InventoryCommandBroadcast message,
            Channel channel)
        {
            if (channel != Channel.Reliable ||
                !message.TryToCommand(out var command) ||
                !TryAcceptEnvelope(
                    connection.ClientId,
                    message.Sequence,
                    out var playerId,
                    out var session))
            {
                return;
            }

            session.SubmitCommand(playerId, GameCommand.ApplyInventory(command));
        }

        private void OnPlayerActionReceived(
            NetworkConnection connection,
            PlayerActionBroadcast message,
            Channel channel)
        {
            if (channel != Channel.Reliable ||
                !TryAcceptEnvelope(
                    connection.ClientId,
                    message.Sequence,
                    out var playerId,
                    out var session))
            {
                return;
            }

            var kind = (PlayerActionCommandKind)message.Kind;
            if (kind == PlayerActionCommandKind.Dash)
            {
                session.SubmitCommand(
                    playerId,
                    GameCommand.ApplyPlayerAction(PlayerActionCommand.Dash()));
                return;
            }
            if (kind != PlayerActionCommandKind.Attack ||
                !message.TryGetDirection(out var direction) ||
                !TryResolveMeleeTarget(playerId, direction.normalized, out var target))
            {
                return;
            }

            session.SubmitCommand(
                playerId,
                GameCommand.ApplyPlayerAction(PlayerActionCommand.Attack(target)));
        }

        private static bool TryResolveMeleeTarget(
            PlayerId attackerId,
            Vector3 direction,
            out PlayerId targetId)
        {
            targetId = default;
            BlockPlayerController attacker = null;
            var controllers = FindObjectsByType<BlockPlayerController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < controllers.Length; i++)
            {
                if (controllers[i].TryGetPlayerId(out var id) && id == attackerId)
                {
                    attacker = controllers[i];
                    break;
                }
            }
            if (attacker == null)
                return false;

            var origin = attacker.transform.position + Vector3.up * 0.8f;
            const float maximumDistance = 2.5f;
            var bestDistance = maximumDistance + 0.01f;
            BlockPlayerController best = null;
            for (var i = 0; i < controllers.Length; i++)
            {
                var candidate = controllers[i];
                if (candidate == attacker || !candidate.TryGetPlayerId(out var candidateId))
                    continue;
                var offset = candidate.transform.position + Vector3.up * 0.8f - origin;
                var planar = Vector3.ProjectOnPlane(offset, Vector3.up);
                var distance = offset.magnitude;
                if (distance > maximumDistance || planar.sqrMagnitude < 0.001f ||
                    Vector3.Dot(direction, planar.normalized) < 0.55f ||
                    distance >= bestDistance)
                {
                    continue;
                }
                if (UnityEngine.Physics.Linecast(
                        origin,
                        origin + offset,
                        out var hit,
                        UnityEngine.Physics.AllLayers,
                        QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<BlockPlayerController>() != candidate)
                {
                    continue;
                }
                best = candidate;
                bestDistance = distance;
                targetId = candidateId;
            }
            return best != null;
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
                networkManager.ServerManager.UnregisterBroadcast<InventoryCommandBroadcast>(
                    OnInventoryCommandReceived);
                networkManager.ServerManager.UnregisterBroadcast<PlayerActionBroadcast>(
                    OnPlayerActionReceived);
                networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            }
            if (ReferenceEquals(PlayerActionCommandBridge.Sender, this))
                PlayerActionCommandBridge.Sender = null;
            networkCallbackRegistered = false;
            ConnectionMapped = null;
            ConnectionUnmapped = null;
        }
    }
}
