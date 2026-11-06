using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace MF2.World.Registry
{
    /// <summary>
    /// Canonical authoring/bootstrap index. Unity object references stop here and are converted
    /// into BlockRegistry's unmanaged ID-to-dense-index table for hot runtime access.
    /// </summary>
    [CreateAssetMenu(fileName = "BlockRegistryManifest", menuName = "MF2/World/Block Registry Manifest")]
    public sealed class BlockRegistryManifest : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string canonicalName;
            [SerializeField] private GameObject prefab;

            public string CanonicalName => canonicalName;
            public GameObject Prefab => prefab;

            internal Entry(string canonicalName, GameObject prefab)
            {
                this.canonicalName = canonicalName;
                this.prefab = prefab;
            }
        }

        [SerializeField] private int idSchemeVersion = StableBlockId.SchemeVersion;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public int IdSchemeVersion => idSchemeVersion;
        public IReadOnlyList<Entry> Entries => entries;

        public BlockRegistry CreateRuntimeRegistry(Allocator allocator = Allocator.Persistent)
        {
            if (idSchemeVersion != StableBlockId.SchemeVersion)
            {
                throw new InvalidOperationException(
                    $"Manifest ID scheme {idSchemeVersion} does not match runtime scheme " +
                    $"{StableBlockId.SchemeVersion}.");
            }

            var names = new string[entries.Length];
            for (var i = 0; i < entries.Length; i++)
                names[i] = entries[i].CanonicalName;
            return new BlockRegistry(names, allocator);
        }

#if UNITY_EDITOR
        internal void ReplaceEntries(Entry[] replacement)
        {
            idSchemeVersion = StableBlockId.SchemeVersion;
            entries = replacement ?? Array.Empty<Entry>();
        }
#endif
    }
}
