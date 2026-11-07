using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using VContainer;
using VContainer.Unity;

namespace MF2
{
    public sealed class ClientPresentationLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // The presentation scope owns exactly one replica. Scene child scopes must inherit
            // this same instance instead of creating an empty scoped replica of their own.
            builder.Register<ClientStateCoordinator>(Lifetime.Singleton)
                .AsSelf()
                .As<IClientReplicatedWorld>()
                .As<IClientWorldViewSource>()
                .As<IEntityStateReader>()
                .As<ILocalInventoryReader>();
        }
    }
}
