using System;
using System.IO;
using System.Linq;
using MF2.EditorUI.Internal;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MF2.EditorUI.Tests
{
    public sealed class FluentGuiTests
    {
        private const string AssemblyDefinitionPath =
            "Assets/Scripts/Editor/FluentIMGUI/MF2.EditorUI.asmdef";

        [Test]
        public void BuilderConfiguration_DoesNotEvaluateOutsideOnGUI()
        {
            Assert.DoesNotThrow(() =>
            {
                var layout = Gui.Vertical()
                    .Padding(12)
                    .Spacing(6f)
                    .Width(300f)
                    .Height(100f)
                    .ExpandWidth(false)
                    .ExpandHeight(false)
                    .Box()
                    .Background(Color.gray);

                var label = Gui.Label("Lazy")
                    .Bold()
                    .FontSize(18)
                    .TextColor(Color.white)
                    .Width(120f)
                    .Height(24f)
                    .Enabled(true);

                var button = Gui.Button("Lazy")
                    .Bold()
                    .FontSize(14)
                    .TextColor(Color.cyan)
                    .Width(100f)
                    .Height(24f)
                    .Enabled(false);

                var field = Gui.FloatField("Lazy", 5f)
                    .Range(0f, 100f)
                    .Width(200f)
                    .Height(20f)
                    .Enabled(true);

                Assert.That(layout, Is.TypeOf<LayoutScopeBuilder>());
                Assert.That(label, Is.TypeOf<LabelBuilder>());
                Assert.That(button, Is.TypeOf<ButtonBuilder>());
                Assert.That(field, Is.TypeOf<FloatFieldBuilder>());
            });
        }

        [Test]
        public void Range_RejectsInvalidBounds()
        {
            Assert.Throws<ArgumentException>(() =>
                Gui.FloatField("Value", 0f).Range(2f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Gui.FloatField("Value", 0f).Range(float.NaN, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Gui.FloatField("Value", 0f).Range(0f, float.PositiveInfinity));
        }

        [Test]
        public void Range_ClampsEvaluatedResult()
        {
            var builder = Gui.FloatField("Value", 5f).Range(0f, 100f);

            Assert.That(builder.EvaluateResult(150f), Is.EqualTo(100f));
            Assert.That(builder.EvaluateResult(-25f), Is.EqualTo(0f));
            Assert.That(builder.EvaluateResult(42f), Is.EqualTo(42f));
        }

        [Test]
        public void EnabledScope_DisposeTwice_RestoresOnlyOnce()
        {
            var originalState = GUI.enabled;
            try
            {
                GUI.enabled = true;
                var scope = Gui.Disabled(true).Begin();
                Assert.That(GUI.enabled, Is.False);

                scope.Dispose();
                Assert.That(GUI.enabled, Is.True);

                GUI.enabled = false;
                scope.Dispose();
                Assert.That(GUI.enabled, Is.False);
            }
            finally
            {
                GUI.enabled = originalState;
            }
        }

        [Test]
        public void NestedEnabledScopes_CannotOverrideDisabledParent_AndRestoreState()
        {
            var originalState = GUI.enabled;
            try
            {
                GUI.enabled = true;

                using (Gui.Disabled(true).Begin())
                {
                    Assert.That(GUI.enabled, Is.False);

                    using (Gui.Enabled(true).Begin())
                    {
                        Assert.That(GUI.enabled, Is.False);
                    }

                    Assert.That(GUI.enabled, Is.False);
                }

                Assert.That(GUI.enabled, Is.True);
            }
            finally
            {
                GUI.enabled = originalState;
            }
        }

        [Test]
        public void StyleCreation_DoesNotMutateEditorLabelOrButtonStyles()
        {
            var labelStyle = EditorStyles.label;
            var buttonStyle = EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).button;
            var labelBefore = StyleSnapshot.Capture(labelStyle);
            var buttonBefore = StyleSnapshot.Capture(buttonStyle);
            var options = default(TextStyleOptions)
                .WithBold()
                .WithFontSize(19)
                .WithTextColor(Color.magenta);

            var labelClone = Styles.CreateWidgetStyle(labelStyle, options);
            var buttonClone = Styles.CreateWidgetStyle(buttonStyle, options);

            Assert.That(labelClone, Is.Not.SameAs(labelStyle));
            Assert.That(buttonClone, Is.Not.SameAs(buttonStyle));
            Assert.That(labelBefore.Matches(labelStyle), Is.True);
            Assert.That(buttonBefore.Matches(buttonStyle), Is.True);
            Assert.That(labelClone.fontStyle, Is.EqualTo(FontStyle.Bold));
            Assert.That(buttonClone.normal.textColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void EditorAssembly_IsEditorOnly_AndRuntimeAssemblyHasNoEditorReference()
        {
            Assert.That(typeof(Gui).Assembly.GetName().Name, Is.EqualTo("MF2.EditorUI"));

            var definitionPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                AssemblyDefinitionPath);
            Assert.That(File.Exists(definitionPath), Is.True);

            var data = JsonUtility.FromJson<AssemblyDefinitionData>(
                File.ReadAllText(definitionPath));
            Assert.That(data.name, Is.EqualTo("MF2.EditorUI"));
            Assert.That(data.includePlatforms, Is.EqualTo(new[] { "Editor" }));

            var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .SingleOrDefault(assembly => assembly.GetName().Name == "Assembly-CSharp");
            Assert.That(runtimeAssembly, Is.Not.Null);
            Assert.That(runtimeAssembly.GetReferencedAssemblies(),
                Has.None.Matches<System.Reflection.AssemblyName>(reference =>
                    reference.Name.StartsWith("UnityEditor", StringComparison.Ordinal)));
        }

        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public string name;
            public string[] includePlatforms;
        }

        private readonly struct StyleSnapshot
        {
            private readonly FontStyle fontStyle;
            private readonly int fontSize;
            private readonly Color normalTextColor;
            private readonly Color hoverTextColor;
            private readonly Color activeTextColor;
            private readonly Color focusedTextColor;

            private StyleSnapshot(
                FontStyle fontStyle,
                int fontSize,
                Color normalTextColor,
                Color hoverTextColor,
                Color activeTextColor,
                Color focusedTextColor)
            {
                this.fontStyle = fontStyle;
                this.fontSize = fontSize;
                this.normalTextColor = normalTextColor;
                this.hoverTextColor = hoverTextColor;
                this.activeTextColor = activeTextColor;
                this.focusedTextColor = focusedTextColor;
            }

            internal static StyleSnapshot Capture(GUIStyle style)
            {
                return new StyleSnapshot(
                    style.fontStyle,
                    style.fontSize,
                    style.normal.textColor,
                    style.hover.textColor,
                    style.active.textColor,
                    style.focused.textColor);
            }

            internal bool Matches(GUIStyle style)
            {
                return fontStyle == style.fontStyle &&
                       fontSize == style.fontSize &&
                       normalTextColor == style.normal.textColor &&
                       hoverTextColor == style.hover.textColor &&
                       activeTextColor == style.active.textColor &&
                       focusedTextColor == style.focused.textColor;
            }
        }
    }
}
