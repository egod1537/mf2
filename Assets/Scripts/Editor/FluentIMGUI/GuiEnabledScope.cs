using System;
using MF2.EditorUI.Internal;
using UnityEngine;

namespace MF2.EditorUI
{
    /// <summary>
    /// Lazy configuration for a nested, composable <see cref="GUI.enabled"/> state scope.
    /// </summary>
    public readonly struct EnabledScopeBuilder
    {
        private readonly bool requestedEnabled;
        private readonly bool isInitialized;

        internal EnabledScopeBuilder(bool requestedEnabled)
        {
            this.requestedEnabled = requestedEnabled;
            isInitialized = true;
        }

        /// <summary>
        /// Applies the requested enabled state without overriding an already-disabled parent and returns a restoration scope.
        /// </summary>
        public EnabledScope Begin()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("Enabled scope builders must be created through Gui.Enabled() or Gui.Disabled().");
            }

            var previousEnabled = GUI.enabled;
            var contextId = GuiStateContext.BeginEnabledScope();

            try
            {
                var scope = new EnabledScope(contextId, previousEnabled);
                GUI.enabled = previousEnabled && requestedEnabled;
                return scope;
            }
            catch
            {
                GuiStateContext.EndEnabledScope(contextId);
                GUI.enabled = previousEnabled;
                throw;
            }
        }
    }

    /// <summary>
    /// Restores one active <see cref="GUI.enabled"/> state when disposed.
    /// </summary>
    public sealed class EnabledScope : IDisposable
    {
        private readonly int contextId;
        private readonly bool previousEnabled;
        private bool isDisposed;

        internal EnabledScope(int contextId, bool previousEnabled)
        {
            this.contextId = contextId;
            this.previousEnabled = previousEnabled;
        }

        /// <summary>
        /// Restores the prior enabled state on the first call; later calls have no effect.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            GuiStateContext.ValidateEnabledScopeEnd(contextId);
            isDisposed = true;

            try
            {
                GUI.enabled = previousEnabled;
            }
            finally
            {
                GuiStateContext.EndEnabledScope(contextId);
            }
        }
    }
}
