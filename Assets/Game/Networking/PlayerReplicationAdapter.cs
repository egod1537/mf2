using System;
using System.Collections.Generic;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using MF2.Entities.Player;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using UnityEngine;
using VContainer;

namespace MF2.Game.Networking
{
    public struct ReplicatedPlayerState
    {
        public Guid PlayerId;
        public int Health;
        public int MaximumHealth;
        public byte LifeState;
        public long Revision;
        public int DashCharges;
        public long NextDashRechargeTick;
        public long LastDashTick;
        public long LastAttackTick;
        public long DashSequence;
        public long AttackSequence;
        public bool Removed;
    }

    public struct PlayerSnapshotBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public ReplicatedPlayerState[] Players;
    }

    public struct PlayerDeltaBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public ReplicatedPlayerState[] Changes;
    }

    public struct ReplicatedItemStack
    {
        public uint ItemId;
        public int Count;
    }

    public struct OwnerInventoryBroadcast : IBroadcast
    {
        public Guid SessionId;
        public long Generation;
        public long TickIndex;
        public Guid PlayerId;
        public long Revision;
        public int SelectedQuickbarSlot;
        public ReplicatedItemStack[] Bag;
        public ReplicatedItemStack[] Quickbar;
        public ReplicatedItemStack[] Equipment;
    }

    /// <summary>
    /// FishNet boundary for authoritative player snapshots and the client-only read replica. It
    /// also binds client-spawned player prefabs after both their domain id and replica are ready.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(ServerSessionCoordinator))]
    public sealed class PlayerReplicationAdapter : MonoBehaviour
    {
        private readonly Dictionary<int, PlayerPresentationBindingGate> clientBindings =
            new Dictionary<int, PlayerPresentationBindingGate>();
        private NetworkManager networkManager;
        private ServerSessionCoordinator coordinator;
        private GameCommandNetworkAdapter commandAdapter;
        private ClientPresentationCoordinator clientPresentation;
        private ClientStateCoordinator clientState;
        private GameSession subscribedSession;
        private bool networkCallbacksRegistered;
        private bool coordinatorCallbacksRegistered;

        [Inject]
        private void Construct(ClientPresentationCoordinator presentation)
        {
            clientPresentation = presentation;
        }

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            coordinator = GetComponent<ServerSessionCoordinator>();
            commandAdapter = GetComponent<GameCommandNetworkAdapter>();
        }

        private void OnEnable()
        {
            coordinator ??= GetComponent<ServerSessionCoordinator>();
            if (coordinatorCallbacksRegistered)
                return;
            coordinator.SessionStarted += OnSessionStarted;
            coordinator.SessionStopping += OnSessionStopping;
            coordinatorCallbacksRegistered = true;

            if (networkCallbacksRegistered && coordinator.TryGetSession(out var activeSession))
                OnSessionStarted(activeSession);
        }

        private void Start()
        {
            clientPresentation.PresentationReady += OnPresentationReady;
            clientPresentation.PresentationStopping += OnPresentationStopping;
            if (clientPresentation.TryGetState(out var readyState))
                OnPresentationReady(readyState);

            networkManager.ClientManager.RegisterBroadcast<PlayerSnapshotBroadcast>(OnSnapshotReceived);
            networkManager.ClientManager.RegisterBroadcast<PlayerDeltaBroadcast>(OnDeltaReceived);
            networkManager.ClientManager.RegisterBroadcast<OwnerInventoryBroadcast>(
                OnOwnerInventoryReceived);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ClientManager.Objects.OnSpawnedAdd += OnClientSpawned;
            networkManager.ClientManager.Objects.OnSpawnedRemove += OnClientDespawned;
            networkManager.ClientManager.Objects.OnSpawnedClear += OnClientSpawnedCleared;
            networkManager.ServerManager.OnAuthenticationResult += OnServerAuthenticationResult;
            commandAdapter.ConnectionMapped += OnConnectionMapped;
            networkCallbacksRegistered = true;

            foreach (var pair in networkManager.ClientManager.Objects.Spawned)
                OnClientSpawned(pair.Key, pair.Value);
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
            if (networkManager != null && networkManager.IsServerStarted)
            {
                var snapshot = startedSession.CaptureSnapshot();
                var players = new ReplicatedPlayerState[snapshot.Players.Count];
                for (var i = 0; i < players.Length; i++)
                    players[i] = ToReplicated(snapshot.Players[i], false);
                networkManager.ServerManager.Broadcast(new PlayerSnapshotBroadcast
                {
                    SessionId = snapshot.Id.Value,
                    Generation = snapshot.Generation,
                    TickIndex = snapshot.TickIndex,
                    Players = players
                });
            }
        }

        private void OnSessionStopping(GameSession endingSession)
        {
            if (!ReferenceEquals(subscribedSession, endingSession))
                return;
            subscribedSession.TickCompleted -= OnSessionTickCompleted;
            subscribedSession = null;
        }

        private void OnServerAuthenticationResult(NetworkConnection connection, bool authenticated)
        {
            if (!authenticated || !coordinator.TryGetSession(out var activeSession))
                return;

            var snapshot = activeSession.CaptureSnapshot();
            var players = new ReplicatedPlayerState[snapshot.Players.Count];
            for (var i = 0; i < players.Length; i++)
                players[i] = ToReplicated(snapshot.Players[i], false);
            networkManager.ServerManager.Broadcast(connection, new PlayerSnapshotBroadcast
            {
                SessionId = snapshot.Id.Value,
                Generation = snapshot.Generation,
                TickIndex = snapshot.TickIndex,
                Players = players
            });
        }

        private void OnSessionTickCompleted(SessionTickResult result)
        {
            if (result.PlayerChanges.Count == 0 || subscribedSession == null)
                return;

            var changes = new ReplicatedPlayerState[result.PlayerChanges.Count];
            for (var i = 0; i < changes.Length; i++)
            {
                var change = result.PlayerChanges[i];
                changes[i] = change.Kind == PlayerStateChangeKind.Removed
                    ? new ReplicatedPlayerState
                    {
                        PlayerId = change.PlayerId.Value,
                        Revision = change.Revision,
                        Removed = true
                    }
                    : ToReplicated(change.Snapshot, false);
            }

            networkManager.ServerManager.Broadcast(new PlayerDeltaBroadcast
            {
                SessionId = subscribedSession.Id.Value,
                Generation = subscribedSession.Generation,
                TickIndex = result.TickIndex,
                Changes = changes
            });

            for (var i = 0; i < result.PlayerChanges.Count; i++)
            {
                var change = result.PlayerChanges[i];
                if (change.Kind == PlayerStateChangeKind.Upserted &&
                    commandAdapter.TryGetConnectionId(change.PlayerId, out var connectionId))
                {
                    SendOwnerInventory(connectionId, change.Snapshot, result.TickIndex);
                }
            }
        }

        private void OnConnectionMapped(int connectionId, PlayerId playerId)
        {
            if (subscribedSession == null ||
                !subscribedSession.Players.TryGet(playerId, out var model))
                return;
            SendOwnerInventory(connectionId, model, subscribedSession.TickIndex);
        }

        private void SendOwnerInventory(int connectionId, in PlayerModel model, long tickIndex)
        {
            if (subscribedSession == null ||
                !networkManager.ServerManager.Clients.TryGetValue(connectionId, out var connection))
                return;
            var inventory = model.Inventory;
            networkManager.ServerManager.Broadcast(connection, new OwnerInventoryBroadcast
            {
                SessionId = subscribedSession.Id.Value,
                Generation = subscribedSession.Generation,
                TickIndex = tickIndex,
                PlayerId = model.Id.Value,
                Revision = model.Revision,
                SelectedQuickbarSlot = inventory.SelectedQuickbarSlot,
                Bag = ToReplicated(inventory.CaptureSlots(InventoryContainerKind.Bag)),
                Quickbar = ToReplicated(inventory.CaptureSlots(InventoryContainerKind.Quickbar)),
                Equipment = ToReplicated(inventory.CaptureSlots(InventoryContainerKind.Equipment))
            });
        }

        private void OnSnapshotReceived(PlayerSnapshotBroadcast message, Channel channel)
        {
            if (clientState == null || message.SessionId == Guid.Empty || message.Generation <= 0)
                return;

            var sources = message.Players ?? Array.Empty<ReplicatedPlayerState>();
            var snapshots = new PlayerModel[sources.Length];
            for (var i = 0; i < sources.Length; i++)
            {
                if (!TryCreateModel(sources[i], out snapshots[i]))
                    return;
            }
            clientState.ApplyPlayerSnapshot(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex,
                snapshots);
        }

        private void OnDeltaReceived(PlayerDeltaBroadcast message, Channel channel)
        {
            if (clientState == null || message.SessionId == Guid.Empty || message.Generation <= 0)
                return;

            var sources = message.Changes ?? Array.Empty<ReplicatedPlayerState>();
            var changes = new PlayerStateChange[sources.Length];
            for (var i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source.PlayerId == Guid.Empty || source.Revision < 0)
                    return;
                var id = new PlayerId(source.PlayerId);
                if (source.Removed)
                {
                    changes[i] = PlayerStateChange.Removed(id, source.Revision);
                }
                else
                {
                    if (!TryCreateModel(source, out var snapshot))
                        return;
                    changes[i] = PlayerStateChange.Upserted(snapshot);
                }
            }
            clientState.ApplyPlayerDelta(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex,
                changes);
        }

        private void OnOwnerInventoryReceived(OwnerInventoryBroadcast message, Channel channel)
        {
            if (clientState == null || message.SessionId == Guid.Empty ||
                message.PlayerId == Guid.Empty || message.Generation <= 0 ||
                !TryCreateStacks(message.Bag, InventoryState.BagSlotCount, out var bag) ||
                !TryCreateStacks(message.Quickbar, InventoryState.QuickbarSlotCount, out var quickbar) ||
                !TryCreateStacks(message.Equipment, InventoryState.EquipmentSlotCount, out var equipment))
                return;

            InventoryState inventory;
            try
            {
                inventory = new InventoryState(
                    bag, quickbar, equipment, message.SelectedQuickbarSlot);
            }
            catch (ArgumentException)
            {
                return;
            }

            clientState.ApplyLocalInventory(
                new SessionId(message.SessionId),
                message.Generation,
                message.TickIndex,
                new PlayerId(message.PlayerId),
                message.Revision,
                inventory);
        }

        private void OnClientSpawned(int objectId, NetworkObject networkObject)
        {
            if (networkObject == null ||
                !networkObject.TryGetComponent<BlockPlayerController>(out var controller))
            {
                return;
            }

            var binding = controller.PresentationBinding;
            if (clientBindings.TryGetValue(objectId, out var previous) && previous != binding)
                previous?.ClearDependencies();
            clientBindings[objectId] = binding;
            if (clientState != null)
                binding.SetDependencies(clientState);
        }

        private void OnClientDespawned(int objectId, NetworkObject networkObject)
        {
            if (!clientBindings.Remove(objectId, out var binding) || binding == null)
                return;
            binding.ResetForReuse();
        }

        private void OnClientSpawnedCleared()
        {
            foreach (var binding in clientBindings.Values)
            {
                binding?.ResetForReuse();
            }
            clientBindings.Clear();
        }

        private void OnPresentationReady(ClientStateCoordinator state)
        {
            clientState = state;
            foreach (var binding in clientBindings.Values)
                binding?.SetDependencies(state);
        }

        private void OnPresentationStopping(ClientStateCoordinator state)
        {
            if (!ReferenceEquals(clientState, state))
                return;
            foreach (var binding in clientBindings.Values)
                binding?.ClearDependencies(state);
            clientState = null;
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;
            OnClientSpawnedCleared();
            clientState?.ClearPlayers();
        }

        private static ReplicatedPlayerState ToReplicated(in PlayerModel snapshot, bool removed) =>
            new ReplicatedPlayerState
            {
                PlayerId = snapshot.Id.Value,
                Health = snapshot.Health,
                MaximumHealth = snapshot.MaximumHealth,
                LifeState = (byte)snapshot.LifeState,
                Revision = snapshot.Revision,
                DashCharges = snapshot.Actions.DashCharges,
                NextDashRechargeTick = snapshot.Actions.NextDashRechargeTick,
                LastDashTick = snapshot.Actions.LastDashTick,
                LastAttackTick = snapshot.Actions.LastAttackTick,
                DashSequence = snapshot.Actions.DashSequence,
                AttackSequence = snapshot.Actions.AttackSequence,
                Removed = removed
            };

        private static ReplicatedItemStack[] ToReplicated(ItemStack[] source)
        {
            var result = new ReplicatedItemStack[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                result[i] = new ReplicatedItemStack
                {
                    ItemId = source[i].ItemId,
                    Count = source[i].Count
                };
            }
            return result;
        }

        private static bool TryCreateStacks(
            ReplicatedItemStack[] source,
            int expectedLength,
            out ItemStack[] result)
        {
            result = null;
            if (source == null || source.Length != expectedLength)
                return false;
            result = new ItemStack[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item.ItemId == 0 && item.Count == 0)
                    continue;
                if (!ItemCatalog.IsKnown(item.ItemId))
                    return false;
                try
                {
                    result[i] = new ItemStack(item.ItemId, item.Count);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryCreateModel(
            in ReplicatedPlayerState source,
            out PlayerModel snapshot)
        {
            snapshot = default;
            if (source.PlayerId == Guid.Empty ||
                source.MaximumHealth <= 0 ||
                source.Health < 0 ||
                source.Health > source.MaximumHealth ||
                source.LifeState > (byte)PlayerLifeState.Dead ||
                source.Revision < 0 ||
                source.DashCharges < 0 ||
                source.DashCharges > PlayerActionState.MaximumDashCharges ||
                source.NextDashRechargeTick < 0 ||
                source.DashSequence < 0 ||
                source.AttackSequence < 0)
            {
                return false;
            }

            snapshot = new PlayerModel(
                new PlayerId(source.PlayerId),
                source.Health,
                source.MaximumHealth,
                (PlayerLifeState)source.LifeState,
                source.Revision,
                null,
                new PlayerActionState(
                    source.DashCharges,
                    source.NextDashRechargeTick,
                    source.LastDashTick,
                    source.LastAttackTick,
                    source.DashSequence,
                    source.AttackSequence));
            return true;
        }

        private void OnDisable()
        {
            UnsubscribeCoordinator();
            if (subscribedSession != null)
                subscribedSession.TickCompleted -= OnSessionTickCompleted;
            subscribedSession = null;
        }

        private void UnsubscribeCoordinator()
        {
            if (!coordinatorCallbacksRegistered || coordinator == null)
                return;
            coordinator.SessionStarted -= OnSessionStarted;
            coordinator.SessionStopping -= OnSessionStopping;
            coordinatorCallbacksRegistered = false;
        }

        private void OnDestroy()
        {
            UnsubscribeCoordinator();
            if (subscribedSession != null)
                subscribedSession.TickCompleted -= OnSessionTickCompleted;
            if (clientPresentation != null)
            {
                clientPresentation.PresentationReady -= OnPresentationReady;
                clientPresentation.PresentationStopping -= OnPresentationStopping;
            }
            OnClientSpawnedCleared();
            if (!networkCallbacksRegistered || networkManager == null)
                return;

            var clientManager = networkManager.ClientManager;
            if (clientManager != null)
            {
                clientManager.UnregisterBroadcast<PlayerSnapshotBroadcast>(OnSnapshotReceived);
                clientManager.UnregisterBroadcast<PlayerDeltaBroadcast>(OnDeltaReceived);
                clientManager.UnregisterBroadcast<OwnerInventoryBroadcast>(
                    OnOwnerInventoryReceived);
                clientManager.OnClientConnectionState -= OnClientConnectionState;
                var spawnedObjects = clientManager.Objects;
                if (spawnedObjects != null)
                {
                    spawnedObjects.OnSpawnedAdd -= OnClientSpawned;
                    spawnedObjects.OnSpawnedRemove -= OnClientDespawned;
                    spawnedObjects.OnSpawnedClear -= OnClientSpawnedCleared;
                }
            }

            if (networkManager.ServerManager != null)
                networkManager.ServerManager.OnAuthenticationResult -= OnServerAuthenticationResult;
            if (commandAdapter != null)
                commandAdapter.ConnectionMapped -= OnConnectionMapped;
            networkCallbacksRegistered = false;
        }
    }
}
