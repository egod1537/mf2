using System.Reflection;
using System.Text.RegularExpressions;
using MF2.Game.Domain;
using MF2.Game.Lifecycle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace MF2.Game.Tests.Editor
{
    public sealed class LifetimeScopeTopologyTests
    {
        [Test]
        public void ClientPresentationScope_OwnsReplicaOutsideApplicationContainer()
        {
            var rootObject = new GameObject("Application Scope Test");
            var root = rootObject.AddComponent<LifetimeScope>();
            ClientPresentationLifetimeScope clientScope = null;
            LifetimeScope sceneChild = null;

            try
            {
                root.autoRun = false;
                root.Build();
                clientScope = root.CreateChild<ClientPresentationLifetimeScope>(
                    childScopeName: "Client Presentation Scope Test");
                if (clientScope.Container == null)
                    clientScope.Build();

                Assert.That(
                    () => root.Container.Resolve<ClientStateCoordinator>(),
                    Throws.Exception);

                var concrete = clientScope.Container.Resolve<ClientStateCoordinator>();
                var entityReader = clientScope.Container.Resolve<IEntityStateReader>();
                var worldReader = clientScope.Container.Resolve<IClientReplicatedWorld>();
                sceneChild = clientScope.CreateChild(
                    childScopeName: "Scene Child Replica Inheritance Test");
                if (sceneChild.Container == null)
                    sceneChild.Build();
                var childReader = sceneChild.Container.Resolve<IClientReplicatedWorld>();

                Assert.That(entityReader, Is.SameAs(concrete));
                Assert.That(worldReader, Is.SameAs(concrete));
                Assert.That(childReader, Is.SameAs(concrete));
                Assert.That(clientScope.Parent, Is.SameAs(root));
            }
            finally
            {
                sceneChild?.DisposeCore();
                if (sceneChild != null)
                    Object.DestroyImmediate(sceneChild.gameObject);
                clientScope?.DisposeCore();
                root.DisposeCore();
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void ServerAndClientScopes_AreDeclaredAsSeparateApplicationChildren()
        {
            Assert.That(typeof(ServerSessionLifetimeScope).BaseType, Is.EqualTo(typeof(LifetimeScope)));
            Assert.That(typeof(ClientPresentationLifetimeScope).BaseType, Is.EqualTo(typeof(LifetimeScope)));
            Assert.That(
                typeof(ServerSessionLifetimeScope).IsAssignableFrom(
                    typeof(ClientPresentationLifetimeScope)),
                Is.False);
        }

        [Test]
        public void ClientPresentationStop_ContinuesAfterSubscriberFailureAndClearsState()
        {
            var rootObject = new GameObject("Client Presentation Failure Isolation Test");
            var coordinator = rootObject.AddComponent<ClientPresentationCoordinator>();
            var state = new ClientStateCoordinator();
            var secondSubscriberCalled = false;

            try
            {
                typeof(ClientPresentationCoordinator)
                    .GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(coordinator, state);
                coordinator.PresentationStopping += _ =>
                    throw new System.InvalidOperationException("presentation stop failure");
                coordinator.PresentationStopping += _ => secondSubscriberCalled = true;
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: presentation stop failure"));

                Assert.DoesNotThrow(coordinator.EndClientPresentation);
                Assert.That(secondSubscriberCalled, Is.True);
                Assert.That(coordinator.TryGetState(out _), Is.False);
                Assert.That(
                    coordinator.State,
                    Is.EqualTo(ClientPresentationCoordinatorState.NoPresentation));
            }
            finally
            {
                state.Dispose();
                Object.DestroyImmediate(rootObject);
            }
        }
    }
}
