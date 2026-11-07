using System;
using MF2.Game.Domain;
using UnityEngine;

namespace MF2.Entities.Player
{
    public enum PlayerPresentationBindingState : byte
    {
        Constructed,
        WaitingForDependencies,
        WaitingForNetwork,
        Bound,
        Unbound,
        Destroyed
    }

    /// <summary>
    /// Joins two independently ordered readiness signals for a FishNet-spawned player: the
    /// client replica dependency supplied by the application adapter and the synchronized domain
    /// identity supplied by NetworkBehaviour callbacks.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EntityViewBinder))]
    public sealed class PlayerPresentationBindingGate : MonoBehaviour
    {
        [SerializeField] private EntityViewBinder viewBinder;

        private IEntityStateReader reader;
        private PlayerId playerId;
        private bool dependenciesReady;
        private bool networkReady;
        private bool destroyed;

        public PlayerPresentationBindingState State { get; private set; } =
            PlayerPresentationBindingState.Constructed;
        public bool DependenciesReady => dependenciesReady;
        public bool NetworkReady => networkReady;
        public bool IsBound => !destroyed && ResolveBinder(false)?.IsBound == true;

        public void Configure(EntityViewBinder binder)
        {
            if (binder == null)
                throw new ArgumentNullException(nameof(binder));
            if (ReferenceEquals(viewBinder, binder))
                return;

            UnbindCurrent();
            viewBinder = binder;
            UpdateWaitingState();
        }

        public void SetDependencies(IEntityStateReader stateReader)
        {
            ThrowIfDestroyed();
            if (stateReader == null)
                throw new ArgumentNullException(nameof(stateReader));
            if (dependenciesReady && ReferenceEquals(reader, stateReader))
            {
                TryBind();
                return;
            }

            UnbindCurrent();
            reader = stateReader;
            dependenciesReady = true;
            TryBind();
        }

        public void ClearDependencies(IEntityStateReader expectedReader = null)
        {
            if (destroyed ||
                (expectedReader != null && !ReferenceEquals(reader, expectedReader)))
            {
                return;
            }

            UnbindCurrent();
            reader = null;
            dependenciesReady = false;
            UpdateWaitingState();
        }

        public void SetNetworkReady(PlayerId id)
        {
            ThrowIfDestroyed();
            if (id.Value == Guid.Empty)
                throw new ArgumentException("Network readiness requires a valid player id.", nameof(id));
            if (networkReady && playerId == id)
            {
                TryBind();
                return;
            }

            UnbindCurrent();
            playerId = id;
            networkReady = true;
            TryBind();
        }

        public void SetNetworkUnavailable()
        {
            if (destroyed)
                return;

            UnbindCurrent();
            playerId = default;
            networkReady = false;
            UpdateWaitingState();
        }

        public void ResetForReuse()
        {
            if (destroyed)
                return;

            UnbindCurrent();
            reader = null;
            playerId = default;
            dependenciesReady = false;
            networkReady = false;
            State = PlayerPresentationBindingState.Unbound;
        }

        private void Awake()
        {
            ResolveBinder();
            UpdateWaitingState();
        }

        private void OnDisable()
        {
            ResetForReuse();
        }

        private void OnDestroy()
        {
            if (destroyed)
                return;
            ResetForReuse();
            destroyed = true;
            State = PlayerPresentationBindingState.Destroyed;
        }

        private void TryBind()
        {
            if (!dependenciesReady || !networkReady)
            {
                UpdateWaitingState();
                return;
            }

            ResolveBinder().Bind(playerId, reader);
            State = PlayerPresentationBindingState.Bound;
        }

        private void UnbindCurrent()
        {
            ResolveBinder(false)?.Unbind();
        }

        private void UpdateWaitingState()
        {
            if (destroyed)
            {
                State = PlayerPresentationBindingState.Destroyed;
                return;
            }
            if (!dependenciesReady)
            {
                State = PlayerPresentationBindingState.WaitingForDependencies;
                return;
            }
            State = networkReady
                ? PlayerPresentationBindingState.Bound
                : PlayerPresentationBindingState.WaitingForNetwork;
        }

        private EntityViewBinder ResolveBinder(bool required = true)
        {
            if (viewBinder != null)
                return viewBinder;
            if (TryGetComponent(out EntityViewBinder binder))
                return viewBinder = binder;
            if (required)
            {
                throw new InvalidOperationException(
                    "PlayerPresentationBindingGate requires an EntityViewBinder.");
            }
            return null;
        }

        private void ThrowIfDestroyed()
        {
            if (destroyed)
                throw new ObjectDisposedException(nameof(PlayerPresentationBindingGate));
        }
    }
}
