namespace MF2.EditorUI
{
    /// <summary>
    /// Entry point for lazy fluent IMGUI layouts and controls.
    /// </summary>
    public static class Gui
    {
        /// <summary>
        /// Creates a lazy vertical layout builder; drawing starts only when <see cref="LayoutScopeBuilder.Begin"/> is called.
        /// </summary>
        public static LayoutScopeBuilder Vertical()
        {
            return LayoutScopeBuilder.Create(LayoutDirection.Vertical);
        }

        /// <summary>
        /// Creates a lazy horizontal layout builder; drawing starts only when <see cref="LayoutScopeBuilder.Begin"/> is called.
        /// </summary>
        public static LayoutScopeBuilder Horizontal()
        {
            return LayoutScopeBuilder.Create(LayoutDirection.Horizontal);
        }

        /// <summary>
        /// Creates a lazy enabled-state builder; <see cref="EnabledScopeBuilder.Begin"/> applies it without overriding a disabled parent.
        /// </summary>
        public static EnabledScopeBuilder Enabled(bool enabled)
        {
            return new EnabledScopeBuilder(enabled);
        }

        /// <summary>
        /// Creates a lazy disabled-state builder; <see cref="EnabledScopeBuilder.Begin"/> applies it and disposal restores the prior state.
        /// </summary>
        public static EnabledScopeBuilder Disabled(bool disabled)
        {
            return new EnabledScopeBuilder(!disabled);
        }

        /// <summary>
        /// Creates a lazy label builder; the label is emitted only when <see cref="LabelBuilder.Draw"/> is called.
        /// </summary>
        public static LabelBuilder Label(string text)
        {
            return new LabelBuilder(text);
        }

        /// <summary>
        /// Creates a lazy button builder; the button is emitted only when <see cref="ButtonBuilder.Draw"/> is called.
        /// </summary>
        public static ButtonBuilder Button(string text)
        {
            return new ButtonBuilder(text);
        }

        /// <summary>
        /// Creates a lazy float-field builder; the field is emitted only when <see cref="FloatFieldBuilder.Draw"/> is called.
        /// </summary>
        public static FloatFieldBuilder FloatField(string label, float value)
        {
            return new FloatFieldBuilder(label, value);
        }
    }
}
