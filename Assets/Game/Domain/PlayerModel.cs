using System;

namespace MF2.Game.Domain
{
    public enum PlayerLifeState : byte
    {
        Alive,
        Dead
    }

    /// <summary>Immutable server snapshot/client replica. Unity physics remains outside it.</summary>
    public readonly struct PlayerModel : IEquatable<PlayerModel>
    {
        public const int DefaultMaximumHealth = 100;

        public PlayerModel(
            PlayerId id,
            int health = DefaultMaximumHealth,
            int maximumHealth = DefaultMaximumHealth,
            PlayerLifeState lifeState = PlayerLifeState.Alive,
            long revision = 0,
            InventoryState inventory = null,
            PlayerActionState? actions = null)
        {
            if (id.Value == Guid.Empty)
                throw new ArgumentException("A player snapshot requires a valid id.", nameof(id));
            if (maximumHealth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumHealth));
            if (health < 0 || health > maximumHealth)
                throw new ArgumentOutOfRangeException(nameof(health));
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            Id = id;
            Health = health;
            MaximumHealth = maximumHealth;
            LifeState = lifeState;
            Revision = revision;
            Inventory = inventory ?? new InventoryState();
            Actions = actions ?? PlayerActionState.Default;
        }

        public PlayerId Id { get; }
        public int Health { get; }
        public int MaximumHealth { get; }
        public PlayerLifeState LifeState { get; }
        public long Revision { get; }
        public InventoryState Inventory { get; }
        public PlayerActionState Actions { get; }

        internal PlayerModel WithHealth(int health)
        {
            var clamped = Math.Max(0, Math.Min(MaximumHealth, health));
            return new PlayerModel(
                Id,
                clamped,
                MaximumHealth,
                clamped == 0 ? PlayerLifeState.Dead : PlayerLifeState.Alive,
                checked(Revision + 1),
                Inventory,
                Actions);
        }

        internal PlayerModel WithInventory(InventoryState inventory) => new PlayerModel(
            Id,
            Health,
            MaximumHealth,
            LifeState,
            checked(Revision + 1),
            inventory ?? throw new ArgumentNullException(nameof(inventory)),
            Actions);

        internal PlayerModel WithActions(PlayerActionState actions) => new PlayerModel(
            Id, Health, MaximumHealth, LifeState, checked(Revision + 1), Inventory, actions);

        public bool Equals(PlayerModel other) =>
            Id == other.Id &&
            Health == other.Health &&
            MaximumHealth == other.MaximumHealth &&
            LifeState == other.LifeState &&
            Revision == other.Revision &&
            Equals(Inventory, other.Inventory) &&
            Actions == other.Actions;

        public override bool Equals(object obj) => obj is PlayerModel other && Equals(other);
        public override int GetHashCode() =>
            HashCode.Combine(Id, Health, MaximumHealth, (byte)LifeState, Revision, Inventory, Actions);

        public static bool operator ==(PlayerModel left, PlayerModel right) => left.Equals(right);
        public static bool operator !=(PlayerModel left, PlayerModel right) => !left.Equals(right);
    }

    public enum PlayerStateChangeKind : byte
    {
        Upserted,
        Removed
    }

    public readonly struct PlayerStateChange
    {
        private PlayerStateChange(
            PlayerStateChangeKind kind,
            PlayerId playerId,
            long revision,
            PlayerModel snapshot)
        {
            Kind = kind;
            PlayerId = playerId;
            Revision = revision;
            Snapshot = snapshot;
        }

        public PlayerStateChangeKind Kind { get; }
        public PlayerId PlayerId { get; }
        public long Revision { get; }
        public PlayerModel Snapshot { get; }
        public bool HasSnapshot => Kind == PlayerStateChangeKind.Upserted;

        public static PlayerStateChange Upserted(PlayerModel snapshot) =>
            new PlayerStateChange(
                PlayerStateChangeKind.Upserted,
                snapshot.Id,
                snapshot.Revision,
                snapshot);

        public static PlayerStateChange Removed(PlayerId playerId, long revision) =>
            new PlayerStateChange(PlayerStateChangeKind.Removed, playerId, revision, default);
    }
}
