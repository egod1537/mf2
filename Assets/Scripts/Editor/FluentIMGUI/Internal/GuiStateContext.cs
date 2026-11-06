using System;
using System.Collections.Generic;

namespace MF2.EditorUI.Internal
{
    internal static class GuiStateContext
    {
        [ThreadStatic]
        private static List<int> enabledScopeIds;

        [ThreadStatic]
        private static int nextEnabledScopeId;

        internal static int BeginEnabledScope()
        {
            if (enabledScopeIds == null)
            {
                enabledScopeIds = new List<int>(4);
            }

            var contextId = ++nextEnabledScopeId;
            enabledScopeIds.Add(contextId);
            return contextId;
        }

        internal static void ValidateEnabledScopeEnd(int contextId)
        {
            if (enabledScopeIds == null ||
                enabledScopeIds.Count == 0 ||
                enabledScopeIds[enabledScopeIds.Count - 1] != contextId)
            {
                throw new InvalidOperationException("GUI enabled scopes must be disposed in reverse order.");
            }
        }

        internal static void EndEnabledScope(int contextId)
        {
            ValidateEnabledScopeEnd(contextId);
            enabledScopeIds.RemoveAt(enabledScopeIds.Count - 1);

            if (enabledScopeIds.Count == 0)
            {
                nextEnabledScopeId = 0;
            }
        }
    }
}
