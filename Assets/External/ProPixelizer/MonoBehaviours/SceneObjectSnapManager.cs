// Copyright Elliot Bentine, 2018-

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProPixelizer
{
    using DepthDictionary = Dictionary<ObjectRenderSnapableKey, ObjectRenderSnapable>;

    /// <summary>
    /// The SceneObjectSnapManager tracks objects for creepless snapping.
    /// 
    /// This is a MonoBehaviour, not a ScriptableObject, because it makes sense for it to be
    /// scoped within the context of a scene. When a new scene is loaded, the previous manager
    /// should be disposed and a new one will be created.
    /// </summary>
    public class SceneObjectSnapManager : MonoBehaviour
    {
        public SceneObjectSnapManager()
        {
            DepthOrderedSnapables = new List<DepthDictionary>();
            for (int i = 0; i < MAX_DEPTH; i++)
            {
                DepthOrderedSnapables.Add(new DepthDictionary());
            }
        }

        public static SceneObjectSnapManager GetSceneSingleton()
        {
            var system = FindAnyObjectByType<SceneObjectSnapManager>(FindObjectsInactive.Include);
            if (system == null)
            {
                var go = new GameObject("ProPixelizer Snap Manager");
                // NB: Cannot remove scene manager from save otherwise scene singleton cannot be located
                go.hideFlags = HideFlags.HideInHierarchy;// | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                system = go.AddComponent<SceneObjectSnapManager>();
            }
            return system;
        }

        public static readonly int MAX_DEPTH = 255;
        List<DepthDictionary> DepthOrderedSnapables;

        /// <summary>
        /// Registers a snapable with the manager and returns a key that can be used to unregister the object later.
        /// </summary>
        public ObjectRenderSnapableKey Register(ObjectRenderSnapable snapable)
        {
            var key = new ObjectRenderSnapableKey(snapable.TransformDepth);
            var dict = GetDictionaryForDepth(key);
            try
            {
                dict.Add(key, snapable);
            } catch (ArgumentException)
            {
                Debug.LogWarning(string.Format("ProPixelizer ObjectSnapManager: failed to register new snapable for ID={}, depth={}", key.ID, key.Depth));
            }
            return key;
        }

        /// <summary>
        /// Removes a snapable from the manager. Verifys that the correct snapable is being removed.
        /// </summary>
        public void Unregister(ObjectRenderSnapableKey key, ObjectRenderSnapable snapable)
        {
            var dict = GetDictionaryForDepth(key);
            if (dict.TryGetValue(key, out var value) && value == snapable)
                dict.Remove(key);
            else
                Debug.LogWarning("ProPixelizer ObjectSnapManager: snapable stored for this key does not match for unregister.");
        }

        /// <summary>
        /// Returns the dictionary used to store ObjectRenderSnapables for a given heirachal depth.
        /// </summary>
        internal DepthDictionary GetDictionaryForDepth(ObjectRenderSnapableKey key) => DepthOrderedSnapables[key.Depth < MAX_DEPTH ? key.Depth : MAX_DEPTH - 1];

        public SnapableEnumerator GetEnumerator() => new SnapableEnumerator(DepthOrderedSnapables, true);

        public SnapableEnumerator Reverse() => new SnapableEnumerator(DepthOrderedSnapables, false);


        /// <summary>
        /// Enumerates through the ordered list of snapables.
        /// </summary>
        public struct SnapableEnumerator
        {
            private readonly List<DepthDictionary> snapables;
            private bool forward;
            private int depth;
            private DepthDictionary.ValueCollection.Enumerator currentDepthEnumerator;

            public SnapableEnumerator(List<DepthDictionary> depthOrderedSnapables, bool forward)
            {
                this.snapables = depthOrderedSnapables;
                this.forward = forward;
                this.depth = default;
                this.currentDepthEnumerator = default;
                Reset();
            }

            public ObjectRenderSnapable Current => this.currentDepthEnumerator.Current;

            public bool MoveNext()
            {
                while (!this.currentDepthEnumerator.MoveNext())
                {
                    // If current depth is empty, advance to next until full depth traversed.
                    if ((forward && this.depth >= SceneObjectSnapManager.MAX_DEPTH - 1) || (!forward && this.depth == 0))
                        return false;
                    this.depth += forward ? 1 : -1;
                    this.currentDepthEnumerator = this.snapables[this.depth].Values.GetEnumerator();
                }
                return true;
            }

            public void Reset()
            {
                this.depth = this.forward ? 0 : SceneObjectSnapManager.MAX_DEPTH - 1;
                this.currentDepthEnumerator = this.snapables[this.depth].Values.GetEnumerator();
            }
        }
    }

    /// <summary>
    /// A key used to uniquely identify an ObjectRenderSnapable in the lists of snapped objects.
    /// </summary>
    public readonly struct ObjectRenderSnapableKey
    {
        public ObjectRenderSnapableKey(int depth) {
            this.Depth = depth;
            this.ID = UnityEngine.Random.Range(1, int.MaxValue);
        }
        public readonly int ID;
        public readonly int Depth;
    }
}