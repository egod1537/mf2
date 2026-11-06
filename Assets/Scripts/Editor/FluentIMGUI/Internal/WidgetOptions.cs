using System;
using UnityEngine;

namespace MF2.EditorUI.Internal
{
    internal readonly struct GuiLayoutOptions
    {
        private readonly bool hasWidth;
        private readonly float width;
        private readonly bool hasHeight;
        private readonly float height;
        private readonly bool hasExpandWidth;
        private readonly bool expandWidth;
        private readonly bool hasExpandHeight;
        private readonly bool expandHeight;

        private GuiLayoutOptions(
            bool hasWidth,
            float width,
            bool hasHeight,
            float height,
            bool hasExpandWidth,
            bool expandWidth,
            bool hasExpandHeight,
            bool expandHeight)
        {
            this.hasWidth = hasWidth;
            this.width = width;
            this.hasHeight = hasHeight;
            this.height = height;
            this.hasExpandWidth = hasExpandWidth;
            this.expandWidth = expandWidth;
            this.hasExpandHeight = hasExpandHeight;
            this.expandHeight = expandHeight;
        }

        internal GuiLayoutOptions WithWidth(float value)
        {
            ValidateDimension(value, nameof(value));
            return new GuiLayoutOptions(
                true,
                value,
                hasHeight,
                height,
                hasExpandWidth,
                expandWidth,
                hasExpandHeight,
                expandHeight);
        }

        internal GuiLayoutOptions WithHeight(float value)
        {
            ValidateDimension(value, nameof(value));
            return new GuiLayoutOptions(
                hasWidth,
                width,
                true,
                value,
                hasExpandWidth,
                expandWidth,
                hasExpandHeight,
                expandHeight);
        }

        internal GuiLayoutOptions WithExpandWidth(bool value)
        {
            return new GuiLayoutOptions(
                hasWidth,
                width,
                hasHeight,
                height,
                true,
                value,
                hasExpandHeight,
                expandHeight);
        }

        internal GuiLayoutOptions WithExpandHeight(bool value)
        {
            return new GuiLayoutOptions(
                hasWidth,
                width,
                hasHeight,
                height,
                hasExpandWidth,
                expandWidth,
                true,
                value);
        }

        internal GUILayoutOption[] CreateOptions()
        {
            var count = (hasWidth ? 1 : 0) +
                        (hasHeight ? 1 : 0) +
                        (hasExpandWidth ? 1 : 0) +
                        (hasExpandHeight ? 1 : 0);
            if (count == 0)
            {
                return Array.Empty<GUILayoutOption>();
            }

            var options = new GUILayoutOption[count];
            var index = 0;
            if (hasWidth)
            {
                options[index++] = GUILayout.Width(width);
            }

            if (hasHeight)
            {
                options[index++] = GUILayout.Height(height);
            }

            if (hasExpandWidth)
            {
                options[index++] = GUILayout.ExpandWidth(expandWidth);
            }

            if (hasExpandHeight)
            {
                options[index] = GUILayout.ExpandHeight(expandHeight);
            }

            return options;
        }

        private static void ValidateDimension(float value, string parameterName)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    value,
                    "GUILayout dimensions must be finite and non-negative.");
            }
        }
    }

    internal readonly struct WidgetOptions
    {
        private readonly GuiLayoutOptions layout;
        private readonly bool hasEnabled;
        private readonly bool enabled;

        private WidgetOptions(GuiLayoutOptions layout, bool hasEnabled, bool enabled)
        {
            this.layout = layout;
            this.hasEnabled = hasEnabled;
            this.enabled = enabled;
        }

        internal bool IsEnabled => !hasEnabled || enabled;

        internal WidgetOptions WithWidth(float value)
        {
            return new WidgetOptions(layout.WithWidth(value), hasEnabled, enabled);
        }

        internal WidgetOptions WithHeight(float value)
        {
            return new WidgetOptions(layout.WithHeight(value), hasEnabled, enabled);
        }

        internal WidgetOptions WithExpandWidth(bool value)
        {
            return new WidgetOptions(layout.WithExpandWidth(value), hasEnabled, enabled);
        }

        internal WidgetOptions WithExpandHeight(bool value)
        {
            return new WidgetOptions(layout.WithExpandHeight(value), hasEnabled, enabled);
        }

        internal WidgetOptions WithEnabled(bool value)
        {
            return new WidgetOptions(layout, true, value);
        }

        internal GUILayoutOption[] CreateOptions()
        {
            return layout.CreateOptions();
        }
    }

    internal readonly struct TextStyleOptions
    {
        internal bool IsBold { get; }
        internal bool HasFontSize { get; }
        internal int FontSize { get; }
        internal bool HasColor { get; }
        internal Color TextColor { get; }

        private TextStyleOptions(
            bool isBold,
            bool hasFontSize,
            int fontSize,
            bool hasColor,
            Color textColor)
        {
            IsBold = isBold;
            HasFontSize = hasFontSize;
            FontSize = fontSize;
            HasColor = hasColor;
            TextColor = textColor;
        }

        internal bool RequiresClone => IsBold || HasFontSize || HasColor;

        internal TextStyleOptions WithBold()
        {
            return new TextStyleOptions(true, HasFontSize, FontSize, HasColor, TextColor);
        }

        internal TextStyleOptions WithFontSize(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Font size must be positive.");
            }

            return new TextStyleOptions(IsBold, true, value, HasColor, TextColor);
        }

        internal TextStyleOptions WithTextColor(Color value)
        {
            return new TextStyleOptions(IsBold, HasFontSize, FontSize, true, value);
        }
    }
}
