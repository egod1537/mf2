using System;

namespace MF2.Game.Domain
{
    /// <summary>Pure domain rule for authoritative health changes.</summary>
    public sealed class HealthSystem
    {
        private readonly PlayerRegistry players;

        public HealthSystem(PlayerRegistry players)
        {
            this.players = players ?? throw new ArgumentNullException(nameof(players));
        }

        public bool ApplyDamage(PlayerId playerId, int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            return players.TryGetPlayer(playerId, out var snapshot) &&
                   players.TrySetHealth(playerId, checked(snapshot.Health - Math.Min(snapshot.Health, amount)));
        }

        public bool Heal(PlayerId playerId, int amount)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            return players.TryGetPlayer(playerId, out var snapshot) &&
                   players.TrySetHealth(
                       playerId,
                       checked(snapshot.Health + Math.Min(snapshot.MaximumHealth - snapshot.Health, amount)));
        }
    }
}
