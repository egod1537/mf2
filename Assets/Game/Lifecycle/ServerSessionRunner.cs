using System;
using FishNet.Managing;
using MF2.Game.Domain;
using UnityEngine;
using VContainer.Unity;

namespace MF2.Game.Lifecycle
{
    /// <summary>
    /// Runs exactly one authoritative domain step from FishNet's post-physics tick phase.
    /// FishNet 4.7.3 invokes OnPostTick after prediction-state reconciliation, OnTick input/
    /// prediction callbacks, and the TimeManager-owned physics simulation.
    /// </summary>
    public sealed class ServerSessionRunner : IInitializable, IDisposable
    {
        private readonly NetworkManager networkManager;
        private readonly GameSession session;
        private bool initialized;

        public ServerSessionRunner(NetworkManager networkManager, GameSession session)
        {
            this.networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public void Initialize()
        {
            if (initialized)
                return;
            // NetworkManager sub-managers are created from its Unity Awake. EditMode composition
            // tests intentionally build the domain scope without running that scene lifecycle.
            if (networkManager.TimeManager == null)
                return;
            networkManager.TimeManager.OnPostTick += OnPostTick;
            session.TickObserverFailed += OnTickObserverFailed;
            initialized = true;
        }

        private void OnPostTick()
        {
            if (!networkManager.IsServerStarted || !session.IsRunning)
                return;
            session.Step(new SimulationStep((float)networkManager.TimeManager.TickDelta));
        }

        public void Dispose()
        {
            if (!initialized)
                return;
            networkManager.TimeManager.OnPostTick -= OnPostTick;
            session.TickObserverFailed -= OnTickObserverFailed;
            initialized = false;
        }

        private static void OnTickObserverFailed(Exception exception)
        {
            Debug.LogException(exception);
        }
    }
}
