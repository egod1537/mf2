using MF2.World.Core;
using UnityEditor;

namespace MF2.World.Editor.Tools
{
    [InitializeOnLoad]
    internal static class WorldHostLifecycleBridge
    {
        static WorldHostLifecycleBridge()
        {
            AssemblyReloadEvents.beforeAssemblyReload += WorldHost.DisposeAllActiveWorlds;
            EditorApplication.quitting += WorldHost.DisposeAllActiveWorlds;
        }
    }
}
