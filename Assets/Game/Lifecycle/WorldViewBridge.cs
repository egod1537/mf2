using MF2.World.Core;
using UnityEngine;
using VContainer;

namespace MF2.Game.Lifecycle
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorldHost))]
    public sealed class WorldViewBridge : MonoBehaviour
    {
        private IClientWorldViewSource clientState;
        private WorldHost host;
        private bool subscribed;

        [Inject]
        private void Construct(IClientWorldViewSource state)
        {
            clientState = state;
            host = GetComponent<WorldHost>();
            if (isActiveAndEnabled)
                Subscribe();
        }

        private void OnEnable()
        {
            if (clientState != null)
                Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed)
                return;
            clientState.WorldReplaced += OnWorldReplaced;
            clientState.WorldFlushed += OnWorldFlushed;
            clientState.WorldClearing += OnWorldClearing;
            subscribed = true;
            if (clientState.TryGetWorld(out var current))
            {
                host.AttachRuntimeWorld(current);
                PublishCollisionReadiness(current);
            }
        }

        private void OnWorldReplaced(WorldCore replacement)
        {
            host.AttachRuntimeWorld(replacement);
            PublishCollisionReadiness(replacement);
        }

        private void OnWorldFlushed(WorldCore current)
        {
            host.RefreshRuntimeWorld(current);
            PublishCollisionReadiness(current);
        }
        private void OnWorldClearing(WorldCore released) => host.DetachRuntimeWorld(released);

        private void PublishCollisionReadiness(WorldCore current)
        {
            if (host.IsCollisionReadyFor(current))
                clientState.MarkCollisionReady(current);
            else
                clientState.MarkCollisionUnavailable(current);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (!subscribed || clientState == null)
                return;
            clientState.WorldReplaced -= OnWorldReplaced;
            clientState.WorldFlushed -= OnWorldFlushed;
            clientState.WorldClearing -= OnWorldClearing;
            if (clientState.TryGetWorld(out var current) && host != null)
            {
                clientState.MarkCollisionUnavailable(current);
                host.DetachRuntimeWorld(current);
            }
            subscribed = false;
        }
    }
}
