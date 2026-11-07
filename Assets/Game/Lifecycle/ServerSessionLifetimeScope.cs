using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Physics;
using MF2.Game.Networking;
using VContainer;
using VContainer.Unity;

namespace MF2
{
    public sealed class ServerSessionLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<PlayerRegistry>(Lifetime.Scoped);
            builder.Register<HealthSystem>(Lifetime.Scoped);
            builder.Register<SimulationWorld>(Lifetime.Scoped);
            builder.Register(
                    _ => new PerlinTerrainGenerator(TerrainConfig.CreateDefault()),
                    Lifetime.Scoped)
                .As<IWorldInitializer>();
            builder.Register<CommandProcessor>(Lifetime.Scoped);
            builder.Register<SessionClock>(Lifetime.Scoped);
            builder.Register<UnityPhysicsAdapter>(Lifetime.Scoped)
                .AsSelf()
                .As<IPhysicsQuery>()
                .As<IPhysicsCommands>()
                .As<IPhysicsBodyRegistry>();
            builder.Register<PlayerNetworkObjectMap>(Lifetime.Scoped)
                .AsSelf()
                .As<IPlayerNetworkObjectMap>();
            builder.Register<ServerWorldCommandAuthorizer>(Lifetime.Scoped)
                .As<IGameCommandAuthorizer>();
            builder.Register<GameSession>(Lifetime.Scoped);
            builder.RegisterEntryPoint<ServerSessionRunner>(Lifetime.Scoped);
        }
    }
}
