using UnityEditor;
using UnityEngine;

namespace MF2.EditorUI.Samples
{
    /// <summary>
    /// Interactive Editor-only showcase for the public fluent IMGUI API.
    /// </summary>
    public sealed class FluentGuiDemoWindow : EditorWindow
    {
        [SerializeField]
        private float speed = 5f;

        [SerializeField]
        private bool canSave = true;

        /// <summary>
        /// Opens or focuses the demo from the Unity Window menu.
        /// </summary>
        [MenuItem("Window/MF2/Fluent IMGUI Demo")]
        public static void ShowWindow()
        {
            GetWindow<FluentGuiDemoWindow>("Fluent IMGUI Demo");
        }

        private void OnGUI()
        {
            using (Gui.Vertical().Padding(12).Spacing(8f).ExpandWidth(true).Begin())
            {
                Gui.Label("Player Settings")
                   .Bold()
                   .FontSize(18)
                   .Draw();

                speed = Gui.FloatField("Speed", speed)
                           .Range(0f, 100f)
                           .Draw();

                using (Gui.Horizontal().Spacing(8f).Begin())
                {
                    if (Gui.Button(canSave ? "Disable Save" : "Enable Save")
                           .Width(110f)
                           .Draw())
                    {
                        canSave = !canSave;
                    }

                    if (Gui.Button("Save").Enabled(canSave).Width(100f).Draw())
                    {
                        Debug.Log($"Saved player speed: {speed}");
                    }

                    if (Gui.Button("Reset").Width(100f).Draw())
                    {
                        speed = 5f;
                    }
                }
            }
        }
    }
}
