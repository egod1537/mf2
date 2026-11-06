using MF2.EditorUI.Internal;
using UnityEditor;
using UnityEngine;

namespace MF2.EditorUI
{
    /// <summary>
    /// Lazy configuration for one text label.
    /// </summary>
    public readonly struct LabelBuilder
    {
        private readonly string text;
        private readonly WidgetOptions layoutOptions;
        private readonly TextStyleOptions styleOptions;

        internal LabelBuilder(string text)
            : this(text, default, default)
        {
        }

        private LabelBuilder(
            string text,
            WidgetOptions layoutOptions,
            TextStyleOptions styleOptions)
        {
            this.text = text;
            this.layoutOptions = layoutOptions;
            this.styleOptions = styleOptions;
        }

        /// <summary>
        /// Returns a builder with bold text; the independent style is resolved only by <see cref="Draw"/>.
        /// </summary>
        public LabelBuilder Bold()
        {
            return new LabelBuilder(text, layoutOptions, styleOptions.WithBold());
        }

        /// <summary>
        /// Returns a builder with the requested positive font size; no IMGUI call occurs until <see cref="Draw"/>.
        /// </summary>
        public LabelBuilder FontSize(int value)
        {
            return new LabelBuilder(text, layoutOptions, styleOptions.WithFontSize(value));
        }

        /// <summary>
        /// Returns a builder with an independent text color applied when <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder TextColor(Color value)
        {
            return new LabelBuilder(text, layoutOptions, styleOptions.WithTextColor(value));
        }

        /// <summary>
        /// Alias for <see cref="TextColor"/> retained for label readability; it never changes GUI or background colors.
        /// </summary>
        public LabelBuilder Color(Color value)
        {
            return TextColor(value);
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout width applied when <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder Width(float value)
        {
            return new LabelBuilder(text, layoutOptions.WithWidth(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout height applied when <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder Height(float value)
        {
            return new LabelBuilder(text, layoutOptions.WithHeight(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-width option applied when <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder ExpandWidth(bool value)
        {
            return new LabelBuilder(text, layoutOptions.WithExpandWidth(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-height option applied when <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder ExpandHeight(bool value)
        {
            return new LabelBuilder(text, layoutOptions.WithExpandHeight(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder whose single label is enabled or disabled only while <see cref="Draw"/> executes.
        /// </summary>
        public LabelBuilder Enabled(bool value)
        {
            return new LabelBuilder(text, layoutOptions.WithEnabled(value), styleOptions);
        }

        /// <summary>
        /// Draws the label immediately in the current IMGUI event.
        /// </summary>
        public void Draw()
        {
            GuiContext.BeforeChildDraw();
            var options = layoutOptions.CreateOptions();
            var style = Styles.GetLabelStyle(styleOptions);

            using (new EditorGUI.DisabledScope(!layoutOptions.IsEnabled))
            {
                GUILayout.Label(text, style, options);
            }
        }
    }

    /// <summary>
    /// Lazy configuration for one button.
    /// </summary>
    public readonly struct ButtonBuilder
    {
        private readonly string text;
        private readonly WidgetOptions layoutOptions;
        private readonly TextStyleOptions styleOptions;

        internal ButtonBuilder(string text)
            : this(text, default, default)
        {
        }

        private ButtonBuilder(
            string text,
            WidgetOptions layoutOptions,
            TextStyleOptions styleOptions)
        {
            this.text = text;
            this.layoutOptions = layoutOptions;
            this.styleOptions = styleOptions;
        }

        /// <summary>
        /// Returns a builder with bold button text; the independent style is resolved only by <see cref="Draw"/>.
        /// </summary>
        public ButtonBuilder Bold()
        {
            return new ButtonBuilder(text, layoutOptions, styleOptions.WithBold());
        }

        /// <summary>
        /// Returns a builder with the requested positive font size; no IMGUI call occurs until <see cref="Draw"/>.
        /// </summary>
        public ButtonBuilder FontSize(int value)
        {
            return new ButtonBuilder(text, layoutOptions, styleOptions.WithFontSize(value));
        }

        /// <summary>
        /// Returns a builder with an independent text color applied when <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder TextColor(Color value)
        {
            return new ButtonBuilder(text, layoutOptions, styleOptions.WithTextColor(value));
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout width applied when <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder Width(float value)
        {
            return new ButtonBuilder(text, layoutOptions.WithWidth(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a fixed GUILayout height applied when <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder Height(float value)
        {
            return new ButtonBuilder(text, layoutOptions.WithHeight(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-width option applied when <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder ExpandWidth(bool value)
        {
            return new ButtonBuilder(text, layoutOptions.WithExpandWidth(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder with a GUILayout expand-height option applied when <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder ExpandHeight(bool value)
        {
            return new ButtonBuilder(text, layoutOptions.WithExpandHeight(value), styleOptions);
        }

        /// <summary>
        /// Returns a builder whose single button is enabled or disabled only while <see cref="Draw"/> executes.
        /// </summary>
        public ButtonBuilder Enabled(bool value)
        {
            return new ButtonBuilder(text, layoutOptions.WithEnabled(value), styleOptions);
        }

        /// <summary>
        /// Draws the button immediately and returns whether it was activated in the current IMGUI event.
        /// </summary>
        public bool Draw()
        {
            GuiContext.BeforeChildDraw();
            var options = layoutOptions.CreateOptions();
            var style = Styles.GetButtonStyle(styleOptions);

            using (new EditorGUI.DisabledScope(!layoutOptions.IsEnabled))
            {
                return GUILayout.Button(text, style, options);
            }
        }
    }

    /// <summary>
    /// Lazy configuration for one editor float field.
    /// </summary>
    public readonly struct FloatFieldBuilder
    {
        private readonly string label;
        private readonly float value;
        private readonly WidgetOptions layoutOptions;
        private readonly bool hasRange;
        private readonly float minimum;
        private readonly float maximum;

        internal FloatFieldBuilder(string label, float value)
            : this(label, value, default, false, 0f, 0f)
        {
        }

        private FloatFieldBuilder(
            string label,
            float value,
            WidgetOptions layoutOptions,
            bool hasRange,
            float minimum,
            float maximum)
        {
            this.label = label;
            this.value = value;
            this.layoutOptions = layoutOptions;
            this.hasRange = hasRange;
            this.minimum = minimum;
            this.maximum = maximum;
        }

        /// <summary>
        /// Returns a builder that clamps the parsed field result on every <see cref="Draw"/>, including the event where typed input becomes valid.
        /// </summary>
        public FloatFieldBuilder Range(float minimum, float maximum)
        {
            if (float.IsNaN(minimum) || float.IsInfinity(minimum))
            {
                throw new System.ArgumentOutOfRangeException(nameof(minimum), minimum, "Range bounds must be finite.");
            }

            if (float.IsNaN(maximum) || float.IsInfinity(maximum))
            {
                throw new System.ArgumentOutOfRangeException(nameof(maximum), maximum, "Range bounds must be finite.");
            }

            if (minimum > maximum)
            {
                throw new System.ArgumentException("Minimum cannot be greater than maximum.", nameof(minimum));
            }

            return new FloatFieldBuilder(label, value, layoutOptions, true, minimum, maximum);
        }

        /// <summary>
        /// Returns a builder whose total label-and-field row receives a fixed GUILayout width at <see cref="Draw"/> time.
        /// </summary>
        public FloatFieldBuilder Width(float value)
        {
            return new FloatFieldBuilder(
                label,
                this.value,
                layoutOptions.WithWidth(value),
                hasRange,
                minimum,
                maximum);
        }

        /// <summary>
        /// Returns a builder whose total label-and-field row receives a fixed GUILayout height at <see cref="Draw"/> time.
        /// </summary>
        public FloatFieldBuilder Height(float value)
        {
            return new FloatFieldBuilder(
                label,
                this.value,
                layoutOptions.WithHeight(value),
                hasRange,
                minimum,
                maximum);
        }

        /// <summary>
        /// Returns a builder whose total label-and-field row receives a GUILayout expand-width option at <see cref="Draw"/> time.
        /// </summary>
        public FloatFieldBuilder ExpandWidth(bool value)
        {
            return new FloatFieldBuilder(
                label,
                this.value,
                layoutOptions.WithExpandWidth(value),
                hasRange,
                minimum,
                maximum);
        }

        /// <summary>
        /// Returns a builder whose total label-and-field row receives a GUILayout expand-height option at <see cref="Draw"/> time.
        /// </summary>
        public FloatFieldBuilder ExpandHeight(bool value)
        {
            return new FloatFieldBuilder(
                label,
                this.value,
                layoutOptions.WithExpandHeight(value),
                hasRange,
                minimum,
                maximum);
        }

        /// <summary>
        /// Returns a builder whose single field is enabled or disabled only while <see cref="Draw"/> executes.
        /// </summary>
        public FloatFieldBuilder Enabled(bool value)
        {
            return new FloatFieldBuilder(
                label,
                this.value,
                layoutOptions.WithEnabled(value),
                hasRange,
                minimum,
                maximum);
        }

        /// <summary>
        /// Draws the float field exactly once and returns its current value, clamped immediately when a range is configured.
        /// </summary>
        public float Draw()
        {
            GuiContext.BeforeChildDraw();
            var options = layoutOptions.CreateOptions();

            using (new EditorGUI.DisabledScope(!layoutOptions.IsEnabled))
            {
                var result = EditorGUILayout.FloatField(label, value, options);
                return EvaluateResult(result);
            }
        }

        internal float EvaluateResult(float result)
        {
            return hasRange ? Mathf.Clamp(result, minimum, maximum) : result;
        }
    }
}
