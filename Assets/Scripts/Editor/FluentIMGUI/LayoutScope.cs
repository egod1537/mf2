using System;
using MF2.EditorUI.Internal;
using UnityEngine;

namespace MF2.EditorUI
{
    /// <summary>
    /// Owns one active GUILayout group and closes it when disposed.
    /// </summary>
    public sealed class LayoutScope : IDisposable
    {
        private readonly LayoutDirection direction;
        private readonly int contextId;
        private bool isDisposed;

        internal LayoutScope(LayoutDirection direction, int contextId)
        {
            this.direction = direction;
            this.contextId = contextId;
        }

        /// <summary>
        /// Ends the active GUILayout group on the first call; later calls have no effect.
        /// Out-of-order disposal throws before closing anything, so callers can still dispose scopes in reverse order.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            GuiContext.ValidateLayoutEnd(contextId);
            isDisposed = true;

            try
            {
                if (direction == LayoutDirection.Vertical)
                {
                    GUILayout.EndVertical();
                }
                else
                {
                    GUILayout.EndHorizontal();
                }
            }
            finally
            {
                GuiContext.EndLayout(contextId);
            }
        }
    }
}
