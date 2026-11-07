using System;
using FishNet.Managing;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using MF2.Game.Networking;
using MF2.Game.Persistence;
using MF2.World.Registry;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ClientPresentationCoordinator))]
    public sealed class ApplicationLifetimeScope : LifetimeScope
    {
        [SerializeField] private BlockRegistryManifest blockRegistryManifest;

        public static ApplicationLifetimeScope Active { get; private set; }

        public void ConfigureBlockRegistry(BlockRegistryManifest manifest)
        {
            blockRegistryManifest = manifest;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (blockRegistryManifest == null)
            {
                throw new InvalidOperationException(
                    "ApplicationLifetimeScope requires the authoritative block registry manifest.");
            }

            builder.RegisterInstance<IBlockIdCatalog>(
                new ManifestBlockIdCatalog(blockRegistryManifest));
            builder.RegisterComponent(GetComponent<NetworkManager>());
            builder.RegisterComponent(GetComponent<SessionController>())
                .AsSelf()
                .As<INetworkSessionFacade>();
            builder.RegisterComponent(GetComponent<PlayerSpawner>());
            builder.RegisterComponent(GetComponent<ServerSessionCoordinator>());
            builder.RegisterComponent(GetComponent<ClientPresentationCoordinator>());
            builder.RegisterComponent(GetComponent<GameCommandNetworkAdapter>())
                .AsSelf()
                .As<IGameCommandSender>()
                .As<IInventoryCommandSender>()
                .As<IPlayerActionCommandSender>();
            builder.RegisterComponent(GetComponent<PlayerReplicationAdapter>());
            builder.RegisterComponent(GetComponent<WorldReplicationAdapter>());
            builder.Register<SessionPersistenceCoordinator>(Lifetime.Singleton);
        }

        protected override void Awake()
        {
            if (Active != null && Active != this)
            {
                autoRun = false;
                return;
            }

            Active = this;
            try
            {
                base.Awake();
                if (!NetworkBootstrap.IsDedicatedProcess())
                {
                    GetComponent<ClientPresentationCoordinator>()
                        .BeginClientPresentation(this);
                }
            }
            catch
            {
                if (Active == this)
                    Active = null;
                throw;
            }
        }

        protected override void OnDestroy()
        {
            if (Active == this)
            {
                GetComponent<ClientPresentationCoordinator>()?.EndClientPresentation();
                Active = null;
            }
            base.OnDestroy();
        }
    }
}
