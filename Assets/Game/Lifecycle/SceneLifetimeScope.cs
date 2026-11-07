using System.Collections.Generic;
using MF2.Game.Networking;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MF2
{
    /// <summary>
    /// Scene presentation scope. It is a client-presentation child, never an application or
    /// server-session child.
    /// </summary>
    [DefaultExecutionOrder(-4900)]
    public sealed class SceneLifetimeScope : LifetimeScope
    {
        public void ConfigureSceneRoots(IEnumerable<GameObject> roots)
        {
            if (autoInjectGameObjects == null)
                autoInjectGameObjects = new List<GameObject>();
            else
                autoInjectGameObjects.Clear();
            if (roots == null)
                return;
            foreach (var root in roots)
            {
                if (root != null &&
                    root != gameObject &&
                    root.GetComponent<LifetimeScope>() == null)
                {
                    autoInjectGameObjects.Add(root);
                }
            }
        }

        protected override LifetimeScope FindParent()
        {
            return Find<ClientPresentationLifetimeScope>();
        }

        protected override void Configure(IContainerBuilder builder)
        {
        }

        protected override void Awake()
        {
            if (NetworkBootstrap.IsDedicatedProcess())
            {
                autoRun = false;
                return;
            }

            var parent = Find<ClientPresentationLifetimeScope>();
            if (parent == null || parent.Container == null)
            {
                throw new System.InvalidOperationException(
                    "Scene presentation requires a ready ClientPresentationLifetimeScope.");
            }
            base.Awake();
        }
    }
}
