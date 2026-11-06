using System;
using System.Collections.Generic;
using MF2.EditorUI.Internal;
using UnityEditor;
using UnityEngine;

namespace MF2.EditorUI
{
    internal static class Styles
    {
        private const int MaxCachedStyles = 128;

        private static readonly Dictionary<LayoutStyleKey, GUIStyle> LayoutStyles =
            new Dictionary<LayoutStyleKey, GUIStyle>();

        private static readonly Dictionary<WidgetStyleKey, GUIStyle> WidgetStyles =
            new Dictionary<WidgetStyleKey, GUIStyle>();

        private static GUISkin cachedSkin;

        internal static GUIStyle GetLayoutStyle(
            bool isBox,
            bool hasPadding,
            int padding,
            bool hasBackground)
        {
            EnsureCurrentSkin();

            var key = new LayoutStyleKey(isBox, hasPadding, padding, hasBackground);
            if (LayoutStyles.TryGetValue(key, out var style))
            {
                return style;
            }

            style = new GUIStyle(isBox ? EditorStyles.helpBox : GUIStyle.none);
            if (hasPadding)
            {
                style.padding = new RectOffset(padding, padding, padding, padding);
            }

            if (hasBackground)
            {
                // GUILayout draws the group's style synchronously from BeginVertical/BeginHorizontal.
                // A white shared texture plus a temporarily scoped GUI.backgroundColor provides a
                // solid tint without allocating per-color textures or mutating a shared GUIStyle.
                style.normal.background = Texture2D.whiteTexture;
            }

            AddBounded(LayoutStyles, key, style);
            return style;
        }

        internal static GUIStyle GetLabelStyle(TextStyleOptions options)
        {
            return GetWidgetStyle(WidgetStyleKind.Label, EditorStyles.label, options);
        }

        internal static GUIStyle GetButtonStyle(TextStyleOptions options)
        {
            return GetWidgetStyle(WidgetStyleKind.Button, GUI.skin.button, options);
        }

        private static GUIStyle GetWidgetStyle(
            WidgetStyleKind kind,
            GUIStyle baseStyle,
            TextStyleOptions options)
        {
            EnsureCurrentSkin();

            if (!options.RequiresClone)
            {
                return baseStyle;
            }

            var key = new WidgetStyleKey(kind, options);
            if (WidgetStyles.TryGetValue(key, out var style))
            {
                return style;
            }

            style = CreateWidgetStyle(baseStyle, options);
            AddBounded(WidgetStyles, key, style);
            return style;
        }

        internal static GUIStyle CreateWidgetStyle(GUIStyle baseStyle, TextStyleOptions options)
        {
            var style = new GUIStyle(baseStyle);
            if (options.IsBold)
            {
                style.fontStyle = FontStyle.Bold;
            }

            if (options.HasFontSize)
            {
                style.fontSize = options.FontSize;
            }

            if (options.HasColor)
            {
                SetTextColor(style, options.TextColor);
            }

            return style;
        }

        private static void AddBounded<TKey>(Dictionary<TKey, GUIStyle> cache, TKey key, GUIStyle style)
        {
            if (cache.Count >= MaxCachedStyles)
            {
                cache.Clear();
            }

            cache.Add(key, style);
        }

        private static void EnsureCurrentSkin()
        {
            if (cachedSkin == GUI.skin)
            {
                return;
            }

            LayoutStyles.Clear();
            WidgetStyles.Clear();
            cachedSkin = GUI.skin;
        }

        private static void SetTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
        }

        private readonly struct LayoutStyleKey : IEquatable<LayoutStyleKey>
        {
            private readonly bool isBox;
            private readonly bool hasPadding;
            private readonly int padding;
            private readonly bool hasBackground;

            internal LayoutStyleKey(bool isBox, bool hasPadding, int padding, bool hasBackground)
            {
                this.isBox = isBox;
                this.hasPadding = hasPadding;
                this.padding = padding;
                this.hasBackground = hasBackground;
            }

            public bool Equals(LayoutStyleKey other)
            {
                return isBox == other.isBox &&
                       hasPadding == other.hasPadding &&
                       padding == other.padding &&
                       hasBackground == other.hasBackground;
            }

            public override bool Equals(object obj)
            {
                return obj is LayoutStyleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = isBox ? 1 : 0;
                    hashCode = (hashCode * 397) ^ (hasPadding ? 1 : 0);
                    hashCode = (hashCode * 397) ^ padding;
                    hashCode = (hashCode * 397) ^ (hasBackground ? 1 : 0);
                    return hashCode;
                }
            }
        }

        private readonly struct WidgetStyleKey : IEquatable<WidgetStyleKey>
        {
            private readonly WidgetStyleKind kind;
            private readonly bool isBold;
            private readonly bool hasFontSize;
            private readonly int fontSize;
            private readonly bool hasColor;
            private readonly Color color;

            internal WidgetStyleKey(WidgetStyleKind kind, TextStyleOptions options)
            {
                this.kind = kind;
                isBold = options.IsBold;
                hasFontSize = options.HasFontSize;
                fontSize = options.FontSize;
                hasColor = options.HasColor;
                color = options.TextColor;
            }

            public bool Equals(WidgetStyleKey other)
            {
                return kind == other.kind &&
                       isBold == other.isBold &&
                       hasFontSize == other.hasFontSize &&
                       fontSize == other.fontSize &&
                       hasColor == other.hasColor &&
                       color.Equals(other.color);
            }

            public override bool Equals(object obj)
            {
                return obj is WidgetStyleKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int)kind;
                    hashCode = (hashCode * 397) ^ (isBold ? 1 : 0);
                    hashCode = (hashCode * 397) ^ (hasFontSize ? 1 : 0);
                    hashCode = (hashCode * 397) ^ fontSize;
                    hashCode = (hashCode * 397) ^ (hasColor ? 1 : 0);
                    hashCode = (hashCode * 397) ^ color.GetHashCode();
                    return hashCode;
                }
            }
        }

        private enum WidgetStyleKind
        {
            Label,
            Button
        }
    }
}
