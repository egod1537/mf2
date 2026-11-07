using System;
using System.Collections.Generic;
using UnityEngine;

namespace SceneToolbar.Editor
{
    internal enum SceneToolbarIconSource
    {
        None,
        BuiltIn,
        Custom
    }

    internal enum SceneToolbarDisplayMode
    {
        IconOnly,
        TextOnly,
        IconAndText
    }

    [Serializable]
    internal sealed class SceneToolbarEntry
    {
        [SerializeField] private string sceneGuid;
        [SerializeField] private string displayName;
        [SerializeField] private SceneToolbarIconSource iconSource;
        [SerializeField] private string builtInIconName;
        [SerializeField] private Texture2D customIcon;
        [SerializeField] private SceneToolbarDisplayMode displayMode = SceneToolbarDisplayMode.IconAndText;

        internal SceneToolbarEntry(string sceneGuid)
        {
            this.sceneGuid = sceneGuid;
        }

        internal string SceneGuid => sceneGuid;

        internal string DisplayName
        {
            get => displayName;
            set => displayName = value;
        }

        internal SceneToolbarIconSource IconSource
        {
            get => iconSource;
            set => iconSource = value;
        }

        internal string BuiltInIconName
        {
            get => builtInIconName;
            set => builtInIconName = value;
        }

        internal Texture2D CustomIcon
        {
            get => customIcon;
            set => customIcon = value;
        }

        internal SceneToolbarDisplayMode DisplayMode
        {
            get => displayMode;
            set => displayMode = value;
        }
    }

    internal sealed class SceneToolbarSettings : ScriptableObject
    {
        internal const int MaxEntries = 8;

        [SerializeField] private List<SceneToolbarEntry> entries = new List<SceneToolbarEntry>();

        internal IReadOnlyList<SceneToolbarEntry> Entries => EnsureEntries();

        internal bool Contains(string sceneGuid)
        {
            return IndexOf(sceneGuid) >= 0;
        }

        internal bool Add(string sceneGuid)
        {
            if (string.IsNullOrWhiteSpace(sceneGuid) ||
                Contains(sceneGuid) ||
                EnsureEntries().Count >= MaxEntries)
            {
                return false;
            }

            EnsureEntries().Add(new SceneToolbarEntry(sceneGuid));
            return true;
        }

        internal bool Remove(string sceneGuid)
        {
            var index = IndexOf(sceneGuid);
            if (index < 0)
            {
                return false;
            }

            EnsureEntries().RemoveAt(index);
            return true;
        }

        internal bool Move(int fromIndex, int toIndex)
        {
            var currentEntries = EnsureEntries();
            if (fromIndex < 0 || fromIndex >= currentEntries.Count ||
                toIndex < 0 || toIndex >= currentEntries.Count ||
                fromIndex == toIndex)
            {
                return false;
            }

            var entry = currentEntries[fromIndex];
            currentEntries.RemoveAt(fromIndex);
            currentEntries.Insert(toIndex, entry);
            return true;
        }

        internal bool UpdateEntry(string sceneGuid, Action<SceneToolbarEntry> update)
        {
            if (update == null)
            {
                return false;
            }

            var entry = Find(sceneGuid);
            if (entry == null)
            {
                return false;
            }

            update(entry);
            return true;
        }

        internal SceneToolbarEntry Find(string sceneGuid)
        {
            var index = IndexOf(sceneGuid);
            return index >= 0 ? EnsureEntries()[index] : null;
        }

        internal int IndexOf(string sceneGuid)
        {
            if (string.IsNullOrWhiteSpace(sceneGuid))
            {
                return -1;
            }

            var currentEntries = EnsureEntries();
            for (var index = 0; index < currentEntries.Count; index++)
            {
                if (string.Equals(currentEntries[index]?.SceneGuid, sceneGuid, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private List<SceneToolbarEntry> EnsureEntries()
        {
            return entries ??= new List<SceneToolbarEntry>();
        }
    }
}
