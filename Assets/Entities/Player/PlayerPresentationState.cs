using MF2.Game.Domain;

namespace MF2.Entities.Player
{
    public readonly struct PlayerPresentationState
    {
        public PlayerPresentationState(
            PlayerId playerId,
            float healthRatio,
            PlayerLifeState lifeState,
            long revision,
            long dashSequence = 0,
            long attackSequence = 0,
            int dashCharges = PlayerActionState.MaximumDashCharges)
        {
            PlayerId = playerId;
            HealthRatio = healthRatio;
            LifeState = lifeState;
            Revision = revision;
            DashSequence = dashSequence;
            AttackSequence = attackSequence;
            DashCharges = dashCharges;
        }

        public PlayerId PlayerId { get; }
        public float HealthRatio { get; }
        public PlayerLifeState LifeState { get; }
        public long Revision { get; }
        public long DashSequence { get; }
        public long AttackSequence { get; }
        public int DashCharges { get; }

        public static PlayerPresentationState FromModel(in PlayerModel snapshot) =>
            new PlayerPresentationState(
                snapshot.Id,
                snapshot.MaximumHealth <= 0
                    ? 0f
                    : (float)snapshot.Health / snapshot.MaximumHealth,
                snapshot.LifeState,
                snapshot.Revision,
                snapshot.Actions.DashSequence,
                snapshot.Actions.AttackSequence,
                snapshot.Actions.DashCharges);
    }

    public interface IPlayerView
    {
        void Apply(in PlayerPresentationState state);
        void ResetView();
    }
}
