using System;
using MF2.EditorUI.Internal;
using UnityEngine;

namespace MF2.EditorUI
{
    /// <summary>
    /// Immutable configuration for a layout group that is evaluated by <see cref="Begin"/>.
    /// </summary>
    public readonly struct LayoutScopeBuilder
    {
        private readonly LayoutDirection direction;
        private readonly bool isInitialized;
        private readonly bool hasPadding;
        private readonly int padding;
        private readonly float spacing;
        private readonly GuiLayoutOptions layoutOptions;
        private readonly bool isBox;
        private readonly bool hasBackground;
        private readonly Color background;

        private LayoutScopeBuilder(
            LayoutDirection direction,
            bool isInitialized,
            bool hasPadding,
            int padding,
            float spacing,
            GuiLayoutOptions layoutOptions,
            bool isBox,
            bool hasBackground,
            Color background)
        {
            this.direction = direction;
            this.isInitialized = isInitialized;
            this.hasPadding = hasPadding;
            this.padding = padding;
            this.spacing = spacing;
            this.layoutOptions = layoutOptions;
            this.isBox = isBox;
            this.hasBackground = hasBackground;
            this.background = background;
        }

        internal static LayoutScopeBuilder Create(LayoutDirection direction)
        {
            return new LayoutScopeBuilder(
                direction,
                true,
                false,
                0,
                0f,
                default,
                false,
                false,
                default);
        }

        /// <summary>
        /// Returns a builder with uniform inner padding; no IMGUI call occurs until <see cref="Begin"/>.
        /// </summary>
        public LayoutScopeBuilder Padding(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Padding cannot be negative.");
            }

            return Copy(hasPadding: true, padding: value);
        }

        /// <summary>
        /// Returns a builder that inserts gaps between direct DSL controls and nested DSL scopes at <see cref="Begin"/>/<c>Draw()</c> time.
        /// Raw <see cref="GUILayout"/> calls are unmanaged and do not participate in automatic spacing.
        /// </summary>
        public LayoutScopeBuilder Spacing(float value)
        {
            ValidateNonNegativeFinite(value, nameof(value));
            return Copy(spacing: value);
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-width option applied when <see cref="Begin"/> evaluates the group.
        /// </summary>
        public LayoutScopeBuilder ExpandWidth(bool value)
        {
            return Copy(layoutOptions: layoutOptions.WithExpandWidth(value));
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-height option applied when <see cref="Begin"/> evaluates the group.
        /// </summary>
        public LayoutScopeBuilder ExpandHeight(bool value)
        {
            return Copy(layoutOptions: layoutOptions.WithExpandHeight(value));
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout width applied when <see cref="Begin"/> evaluates the group.
        /// </summary>
        public LayoutScopeBuilder Width(float value)
        {
            return Copy(layoutOptions: layoutOptions.WithWidth(value));
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout height applied when <see cref="Begin"/> evaluates the group.
        /// </summary>
        public LayoutScopeBuilder Height(float value)
        {
            return Copy(layoutOptions: layoutOptions.WithHeight(value));
        }

        /// <summary>
        /// Returns a builder based on a cloned <see cref="UnityEditor.EditorStyles.helpBox"/> style when <see cref="Begin"/> executes.
        /// </summary>
        public LayoutScopeBuilder Box()
        {
            return Copy(isBox: true);
        }

        /// <summary>
        /// Returns a builder with a tinted solid background drawn when <see cref="Begin"/> evaluates the group during repaint.
        /// </summary>
        public LayoutScopeBuilder Background(Color value)
        {
            return Copy(hasBackground: true, background: value);
        }

        /// <summary>
        /// Begins the configured GUILayout group after all options are applied and returns an idempotent scope that ends it on disposal.
        /// Dispose the scope within the same synchronous, non-reentrant <c>OnGUI</c> callback.
        /// </summary>
        public LayoutScope Begin()
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException("Layout builders must be created through Gui.Vertical() or Gui.Horizontal().");
            }

            var options = layoutOptions.CreateOptions();
            var style = Styles.GetLayoutStyle(isBox, hasPadding, padding, hasBackground);
            var previousBackgroundColor = GUI.backgroundColor;

            GuiContext.BeforeChildDraw();

            try
            {
                if (hasBackground)
                {
                    GUI.backgroundColor = background;
                }

                if (direction == LayoutDirection.Vertical)
                {
                    GUILayout.BeginVertical(style, options);
                }
                else
                {
                    GUILayout.BeginHorizontal(style, options);
                }
            }
            finally
            {
                GUI.backgroundColor = previousBackgroundColor;
            }

            var contextId = 0;
            try
            {
                contextId = GuiContext.BeginLayout(spacing);
                return new LayoutScope(direction, contextId);
            }
            catch
            {
                try
                {
                    if (contextId != 0)
                    {
                        GuiContext.EndLayout(contextId);
                    }
                }
                finally
                {
                    EndLayoutGroup();
                }

                throw;
            }
        }

        private LayoutScopeBuilder Copy(
            bool? hasPadding = null,
            int? padding = null,
            float? spacing = null,
            GuiLayoutOptions? layoutOptions = null,
            bool? isBox = null,
            bool? hasBackground = null,
            Color? background = null)
        {
            return new LayoutScopeBuilder(
                direction,
                isInitialized,
                hasPadding ?? this.hasPadding,
                padding ?? this.padding,
                spacing ?? this.spacing,
                layoutOptions ?? this.layoutOptions,
                isBox ?? this.isBox,
                hasBackground ?? this.hasBackground,
                background ?? this.background);
        }

        private void EndLayoutGroup()
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

        private static void ValidateNonNegativeFinite(float value, string parameterName)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "Value must be finite and non-negative.");
            }
        }
    }

    internal enum LayoutDirection
    {
        Vertical,
        Horizontal
    }
}
