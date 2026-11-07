using System;

namespace MF2.Game.Domain
{
    public enum PlayerActionCommandKind : byte
    {
        None,
        Dash,
        Attack
    }

    public readonly struct PlayerActionCommand
    {
        private PlayerActionCommand(PlayerActionCommandKind kind, PlayerId target)
        {
            Kind = kind;
            Target = target;
        }

        public PlayerActionCommandKind Kind { get; }
        public PlayerId Target { get; }
        public bool IsStructurallyValid => Kind == PlayerActionCommandKind.Dash ||
            (Kind == PlayerActionCommandKind.Attack && Target.Value != Guid.Empty);

        public static PlayerActionCommand Dash() =>
            new PlayerActionCommand(PlayerActionCommandKind.Dash, default);
        public static PlayerActionCommand Attack(PlayerId target) =>
            new PlayerActionCommand(PlayerActionCommandKind.Attack, target);
    }

    public readonly struct PlayerActionState : IEquatable<PlayerActionState>
    {
        public const int MaximumDashCharges = 3;
        public const int DashRechargeTicks = 60;
        public const int DashCooldownTicks = 15;
        public const int AttackCooldownTicks = 15;

        public PlayerActionState(
            int dashCharges,
            long nextDashRechargeTick,
            long lastDashTick,
            long lastAttackTick,
            long dashSequence,
            long attackSequence)
        {
            if (dashCharges < 0 || dashCharges > MaximumDashCharges ||
                nextDashRechargeTick < 0 || dashSequence < 0 || attackSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(dashCharges));
            DashCharges = dashCharges;
            NextDashRechargeTick = nextDashRechargeTick;
            LastDashTick = lastDashTick;
            LastAttackTick = lastAttackTick;
            DashSequence = dashSequence;
            AttackSequence = attackSequence;
        }

        public int DashCharges { get; }
        public long NextDashRechargeTick { get; }
        public long LastDashTick { get; }
        public long LastAttackTick { get; }
        public long DashSequence { get; }
        public long AttackSequence { get; }

        public static PlayerActionState Default => new PlayerActionState(
            MaximumDashCharges, 0, -DashCooldownTicks, -AttackCooldownTicks, 0, 0);

        public bool TryDash(long tick, out PlayerActionState next)
        {
            next = this;
            if (DashCharges <= 0 || tick < LastDashTick + DashCooldownTicks)
                return false;
            next = new PlayerActionState(
                DashCharges - 1,
                DashCharges == MaximumDashCharges ? checked(tick + DashRechargeTicks) : NextDashRechargeTick,
                tick,
                LastAttackTick,
                checked(DashSequence + 1),
                AttackSequence);
            return true;
        }

        public bool TryAttack(long tick, out PlayerActionState next)
        {
            next = this;
            if (tick < LastAttackTick + AttackCooldownTicks)
                return false;
            next = new PlayerActionState(
                DashCharges,
                NextDashRechargeTick,
                LastDashTick,
                tick,
                DashSequence,
                checked(AttackSequence + 1));
            return true;
        }

        public PlayerActionState Advance(long tick)
        {
            if (DashCharges >= MaximumDashCharges || NextDashRechargeTick <= 0 ||
                tick < NextDashRechargeTick)
                return this;
            var charges = DashCharges;
            var nextTick = NextDashRechargeTick;
            while (charges < MaximumDashCharges && tick >= nextTick)
            {
                charges++;
                nextTick = checked(nextTick + DashRechargeTicks);
            }
            if (charges == MaximumDashCharges)
                nextTick = 0;
            return new PlayerActionState(
                charges, nextTick, LastDashTick, LastAttackTick, DashSequence, AttackSequence);
        }

        public bool Equals(PlayerActionState other) =>
            DashCharges == other.DashCharges &&
            NextDashRechargeTick == other.NextDashRechargeTick &&
            LastDashTick == other.LastDashTick &&
            LastAttackTick == other.LastAttackTick &&
            DashSequence == other.DashSequence &&
            AttackSequence == other.AttackSequence;
        public override bool Equals(object obj) => obj is PlayerActionState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            DashCharges, NextDashRechargeTick, LastDashTick, LastAttackTick,
            DashSequence, AttackSequence);
        public static bool operator ==(PlayerActionState left, PlayerActionState right) => left.Equals(right);
        public static bool operator !=(PlayerActionState left, PlayerActionState right) => !left.Equals(right);
    }

    public interface IPlayerActionCommandSender
    {
        bool SendDash();
        bool SendAttack(float directionX, float directionZ);
    }

    /// <summary>Temporary assembly-safe seam for externally spawned player prefabs.</summary>
    public static class PlayerActionCommandBridge
    {
        public static IPlayerActionCommandSender Sender { get; set; }
    }
}
