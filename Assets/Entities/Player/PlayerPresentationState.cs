using MF2.Game.Domain;

namespace MF2.Entities.Player
{
    public readonly struct PlayerPresentationState
    {
        public PlayerPresentationState(
            PlayerId playerId,
            float healthRatio,
            PlayerLifeState lifeState,
            long revision)
        {
            PlayerId = playerId;
            HealthRatio = healthRatio;
            LifeState = lifeState;
            Revision = revision;
        }

        public PlayerId PlayerId { get; }
        public float HealthRatio { get; }
        public PlayerLifeState LifeState { get; }
        public long Revision { get; }

        public static PlayerPresentationState FromModel(in PlayerModel snapshot) =>
            new PlayerPresentationState(
                snapshot.Id,
                snapshot.MaximumHealth <= 0
                    ? 0f
                    : (float)snapshot.Health / snapshot.MaximumHealth,
                snapshot.LifeState,
                snapshot.Revision);
    }

    public interface IPlayerView
    {
        void Apply(in PlayerPresentationState state);
        void ResetView();
    }
}
