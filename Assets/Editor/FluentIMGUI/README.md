# Fluent IMGUI

The implementation lives in `Assets/Scripts/Editor/FluentIMGUI/` and compiles in the
Editor-only `MF2.EditorUI` assembly. Runtime/gameplay assemblies must not reference it.

This is an **IMGUI ergonomics helper**, not a retained-mode UI tree, and it is not an
alternative to UI Toolkit for complex runtime UI.

Use the `MF2.EditorUI` namespace and the `Gui` façade; the name intentionally avoids
ambiguity with `UnityEngine.GUI`. Layout builders are evaluated only by `.Begin()`, while
widget builders are evaluated only by `.Draw()`. Configuration calls never render controls.

```csharp
private float speed = 5f;
private bool canSave = true;

private void OnGUI()
{
    using (Gui.Vertical().Padding(12).Spacing(8).Begin())
    {
        Gui.Label("Player Settings").Bold().FontSize(18).Draw();
        speed = Gui.FloatField("Speed", speed).Range(0f, 100f).Draw();

        using (Gui.Horizontal().Spacing(8).Begin())
        {
            using (Gui.Disabled(!canSave).Begin())
            {
                if (Gui.Button("Save").Width(100).Draw())
                    Debug.Log($"Saved {speed}");
            }

            using (Gui.Enabled(true).Begin())
            {
                if (Gui.Button("Reset").Draw()) speed = 5f;
            }
        }
    }
}
```

`Vertical()` and `Horizontal()` return lazy layout builders, and their disposable scopes
must remain inside the same synchronous `OnGUI()` call. Builder type names follow the
control they configure (`LabelBuilder`, `ButtonBuilder`, and `FloatFieldBuilder`).

`.Spacing()` inserts gaps only between direct children drawn through this DSL. A nested DSL
scope counts as one direct child because its `.Begin()` notifies the parent. Raw
`GUILayout.*`/`EditorGUILayout.*` controls are intentionally unmanaged: mixing them into a
gap-aware scope will not add or advance automatic spacing, so insert explicit
`GUILayout.Space(...)` around raw controls or keep them in a separate nested scope.

Current limitations: no managed spacing for raw controls and no `ScrollView`, `Foldout`,
`EnumPopup`, or `ObjectField` builders. Those are potential future additions, not part of
the initial MVP.
