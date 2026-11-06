using System;
using System.Collections.Generic;
using UnityEngine;

namespace MF2.EditorUI.Internal
{
    internal static class GuiContext
    {
        [ThreadStatic]
        private static List<LayoutFrame> frames;

        [ThreadStatic]
        private static int nextContextId;

        internal static void BeforeChildDraw()
        {
            if (frames == null || frames.Count == 0)
            {
                return;
            }

            var index = frames.Count - 1;
            var frame = frames[index];
            if (frame.HasChild && frame.Spacing > 0f)
            {
                GUILayout.Space(frame.Spacing);
            }

            frame.HasChild = true;
            frames[index] = frame;
        }

        internal static int BeginLayout(float spacing)
        {
            if (frames == null)
            {
                frames = new List<LayoutFrame>(4);
            }

            var contextId = ++nextContextId;
            frames.Add(new LayoutFrame(contextId, spacing));
            return contextId;
        }

        internal static void ValidateLayoutEnd(int contextId)
        {
            if (frames == null || frames.Count == 0 || frames[frames.Count - 1].ContextId != contextId)
            {
                throw new InvalidOperationException("Fluent IMGUI layout scopes must be disposed in reverse order.");
            }
        }

        internal static void EndLayout(int contextId)
        {
            ValidateLayoutEnd(contextId);
            frames.RemoveAt(frames.Count - 1);

            if (frames.Count == 0)
            {
                nextContextId = 0;
            }
        }

        private struct LayoutFrame
        {
            internal readonly int ContextId;
            internal readonly float Spacing;
            internal bool HasChild;

            internal LayoutFrame(int contextId, float spacing)
            {
                ContextId = contextId;
                Spacing = spacing;
                HasChild = false;
            }
        }
    }
}
