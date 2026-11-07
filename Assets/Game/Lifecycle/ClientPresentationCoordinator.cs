using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2.Game.Lifecycle
{
    public enum ClientPresentationCoordinatorState : byte
    {
        NoPresentation,
        Creating,
        Running,
        Stopping
    }

    /// <summary>
    /// Explicit owner and bridge for the client presentation scope. Network adapters live in the
    /// application scope, so they receive the client replica through this coordinator instead of
    /// resolving a child service from their parent container.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClientPresentationCoordinator : MonoBehaviour
    {
        private ClientPresentationLifetimeScope presentationScope;
        private ClientStateCoordinator state;

        public event Action<ClientStateCoordinator> PresentationReady;
        public event Action<ClientStateCoordinator> PresentationStopping;

        public ClientPresentationCoordinatorState State { get; private set; } =
            ClientPresentationCoordinatorState.NoPresentation;
        public ClientPresentationLifetimeScope PresentationScope => presentationScope;

        public void BeginClientPresentation(ApplicationLifetimeScope applicationScope)
        {
            if (applicationScope == null)
                throw new ArgumentNullException(nameof(applicationScope));
            if (state != null)
                return;
            if (State != ClientPresentationCoordinatorState.NoPresentation)
            {
                throw new InvalidOperationException(
                    $"Cannot create client presentation while {State}.");
            }

            State = ClientPresentationCoordinatorState.Creating;
            ClientPresentationLifetimeScope createdScope = null;
            try
            {
                createdScope = applicationScope.CreateChild<ClientPresentationLifetimeScope>(
                    childScopeName: "Client Presentation Lifetime Scope");
                if (createdScope.Container == null)
                    createdScope.Build();
                var createdState = createdScope.Container.Resolve<ClientStateCoordinator>();
                presentationScope = createdScope;
                state = createdState;
                State = ClientPresentationCoordinatorState.Running;
                PublishPresentationEvent(PresentationReady, createdState);
            }
            catch
            {
                DisposeScope(createdScope);
                presentationScope = null;
                state = null;
                State = ClientPresentationCoordinatorState.NoPresentation;
                throw;
            }
        }

        public void EndClientPresentation()
        {
            if (state == null)
                return;

            State = ClientPresentationCoordinatorState.Stopping;
            var endingState = state;
            var endingScope = presentationScope;
            state = null;
            presentationScope = null;
            try
            {
                PublishPresentationEvent(PresentationStopping, endingState);
            }
            finally
            {
                DisposeScope(endingScope);
                State = ClientPresentationCoordinatorState.NoPresentation;
            }
        }

        public bool TryGetState(out ClientStateCoordinator clientState)
        {
            clientState = state;
            return clientState != null;
        }

        private static void DisposeScope(LifetimeScope scope)
        {
            if (scope == null)
                return;
            scope.DisposeCore();
            if (Application.isPlaying)
                Destroy(scope.gameObject);
            else
                DestroyImmediate(scope.gameObject);
        }

        private static void PublishPresentationEvent(
            Action<ClientStateCoordinator> handlers,
            ClientStateCoordinator value)
        {
            if (handlers == null)
                return;
            foreach (Action<ClientStateCoordinator> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void OnDestroy()
        {
            EndClientPresentation();
            PresentationReady = null;
            PresentationStopping = null;
        }
    }
}
