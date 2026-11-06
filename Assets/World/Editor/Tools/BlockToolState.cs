using System;
using MF2.World.Core;
using MF2.World.Registry;
using UnityEditor;

namespace MF2.World.Editor.Tools
{
    internal enum BlockEditMode
    {
        Paint = 0,
        Erase = 1,
        Pick = 2
    }

    internal enum WorkPlaneAxis
    {
        XZ = 0,
        XY = 1,
        YZ = 2
    }

    [InitializeOnLoad]
    internal static class BlockToolState
    {
        private const string KeyPrefix = "MF2.World.BlockTool.";
        private const string ModeKey = KeyPrefix + "Mode";
        private const string PlaneAxisKey = KeyPrefix + "PlaneAxis";
        private const string PlaneOffsetKey = KeyPrefix + "PlaneOffset";
        private const string SelectedNameKey = KeyPrefix + "SelectedName";

        private static BlockEditMode mode;
        private static WorkPlaneAxis planeAxis;
        private static int planeOffset;
        private static string selectedCanonicalName;
        private static uint selectedId;

        static BlockToolState()
        {
            mode = (BlockEditMode)SessionState.GetInt(ModeKey, (int)BlockEditMode.Paint);
            planeAxis = (WorkPlaneAxis)SessionState.GetInt(
                PlaneAxisKey,
                (int)WorkPlaneAxis.XZ);
            planeOffset = SessionState.GetInt(PlaneOffsetKey, 0);
            selectedCanonicalName = SessionState.GetString(SelectedNameKey, string.Empty);
        }

        internal static event Action Changed;

        internal static BlockEditMode Mode
        {
            get => mode;
            set
            {
                if (mode == value)
                    return;
                mode = value;
                SessionState.SetInt(ModeKey, (int)value);
                Changed?.Invoke();
            }
        }

        internal static WorkPlaneAxis PlaneAxis
        {
            get => planeAxis;
            set
            {
                if (planeAxis == value)
                    return;
                planeAxis = value;
                SessionState.SetInt(PlaneAxisKey, (int)value);
                Changed?.Invoke();
            }
        }

        internal static int PlaneOffset
        {
            get => planeOffset;
            set
            {
                if (planeOffset == value)
                    return;
                planeOffset = value;
                SessionState.SetInt(PlaneOffsetKey, value);
                Changed?.Invoke();
            }
        }

        internal static string SelectedCanonicalName => selectedCanonicalName;
        internal static uint SelectedId => selectedId;
        internal static bool HasSelection => selectedId != 0;
        internal static Block SelectedBlock => new Block(selectedId, 0);

        internal static void ResolveSelection(BlockRegistryManifest manifest)
        {
            if (manifest == null || manifest.Entries.Count == 0)
            {
                SetSelection(string.Empty, 0);
                return;
            }

            if (!string.IsNullOrEmpty(selectedCanonicalName))
            {
                for (var i = 0; i < manifest.Entries.Count; i++)
                {
                    var entry = manifest.Entries[i];
                    if (string.Equals(
                            entry.CanonicalName,
                            selectedCanonicalName,
                            StringComparison.Ordinal))
                    {
                        SetSelection(entry.CanonicalName, StableBlockId.Compute(entry.CanonicalName));
                        return;
                    }
                }
            }

            for (var i = 0; i < manifest.Entries.Count; i++)
            {
                var entry = manifest.Entries[i];
                if (entry.CanonicalName == "minecraft:stone")
                {
                    SetSelection(entry.CanonicalName, StableBlockId.Compute(entry.CanonicalName));
                    return;
                }
            }

            var fallback = manifest.Entries[0];
            SetSelection(fallback.CanonicalName, StableBlockId.Compute(fallback.CanonicalName));
        }

        internal static void Select(BlockRegistryManifest.Entry entry)
        {
            if (entry == null)
                return;
            SetSelection(entry.CanonicalName, StableBlockId.Compute(entry.CanonicalName));
        }

        internal static bool SelectById(BlockRegistryManifest manifest, uint stableId)
        {
            if (manifest == null || stableId == 0)
                return false;

            for (var i = 0; i < manifest.Entries.Count; i++)
            {
                var entry = manifest.Entries[i];
                if (StableBlockId.Compute(entry.CanonicalName) != stableId)
                    continue;
                SetSelection(entry.CanonicalName, stableId);
                return true;
            }

            return false;
        }

        private static void SetSelection(string canonicalName, uint stableId)
        {
            canonicalName = canonicalName ?? string.Empty;
            if (selectedId == stableId &&
                string.Equals(selectedCanonicalName, canonicalName, StringComparison.Ordinal))
            {
                return;
            }

            selectedCanonicalName = canonicalName;
            selectedId = stableId;
            SessionState.SetString(SelectedNameKey, canonicalName);
            Changed?.Invoke();
        }
    }
}
