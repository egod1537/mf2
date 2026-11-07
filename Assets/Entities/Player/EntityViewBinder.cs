using System;
using MF2.Game.Domain;
using UnityEngine;

namespace MF2.Entities.Player
{
    /// <summary>
    /// Projects client-replica snapshots into one View. It owns neither the model nor networking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EntityViewBinder : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour viewSource;

        private PlayerId playerId;
        private IEntityStateReader reader;
        private IPlayerView view;
        private bool hasIdentity;
        private long lastRenderedRevision = -1;

        public bool IsBound => hasIdentity && reader != null && view != null;
        public bool HasRenderedState => lastRenderedRevision >= 0;
        public PlayerId PlayerId => playerId;
        public MonoBehaviour ViewSource => viewSource;

        public void Configure(MonoBehaviour entityView)
        {
            if (entityView != null && entityView is not IPlayerView)
                throw new ArgumentException("The view must implement IPlayerView.", nameof(entityView));

            viewSource = entityView;
            view = entityView as IPlayerView;
            if (Application.isPlaying)
                Unbind();
        }

        /// <summary>
        /// Idempotent binding. Subscription happens before the first full read so a concurrent
        /// change cannot leave the View older than the reader.
        /// </summary>
        public bool Bind(PlayerId id, IEntityStateReader stateReader)
        {
            if (id.Value == Guid.Empty)
                throw new ArgumentException("A binder requires a valid player id.", nameof(id));
            if (stateReader == null)
                throw new ArgumentNullException(nameof(stateReader));

            ResolveView();
            if (IsBound && playerId == id && ReferenceEquals(reader, stateReader))
                return Refresh();

            Unbind();
            playerId = id;
            reader = stateReader;
            hasIdentity = true;
            reader.PlayerStateChanged += OnPlayerStateChanged;
            return Refresh();
        }

        public void Unbind()
        {
            if (reader != null)
                reader.PlayerStateChanged -= OnPlayerStateChanged;

            reader = null;
            playerId = default;
            hasIdentity = false;
            lastRenderedRevision = -1;
            ResolveView(false)?.ResetView();
        }

        public bool Refresh()
        {
            if (!IsBound || !reader.TryGetPlayer(playerId, out var snapshot))
            {
                lastRenderedRevision = -1;
                view?.ResetView();
                return false;
            }
            if (snapshot.Revision == lastRenderedRevision)
                return true;

            view.Apply(PlayerPresentationState.FromModel(snapshot));
            lastRenderedRevision = snapshot.Revision;
            return true;
        }

        private void Awake()
        {
            ResolveView();
            view.ResetView();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnPlayerStateChanged(PlayerStateChange change)
        {
            if (!hasIdentity || change.PlayerId != playerId)
                return;
            if (change.Kind == PlayerStateChangeKind.Removed)
            {
                Unbind();
                return;
            }

            Refresh();
        }

        private IPlayerView ResolveView(bool required = true)
        {
            if (view != null)
                return view;
            if (viewSource is IPlayerView configured)
                return view = configured;
            if (TryGetComponent<BlockPlayerView>(out var playerView))
            {
                viewSource = playerView;
                return view = playerView;
            }
            if (required)
                throw new InvalidOperationException("EntityViewBinder requires an IPlayerView.");
            return null;
        }
    }
}
