using System;
using UnityEditor;
using UnityEngine;

namespace MF2.Minecraft.Editor
{
    public sealed class MinecraftTexturePackWindow : EditorWindow
    {
        private static readonly string[] PackLabels =
        {
            "Minecraft Default",
            "Rodrigo"
        };

        private MinecraftTexturePackId selectedPack;
        private bool selectedPackIsValid;
        private string validationMessage;
        private GUIStyle titleStyle;

        [MenuItem("Tools/Minecraft/Texture Pack Switcher")]
        public static void ShowWindow()
        {
            var window = GetWindow<MinecraftTexturePackWindow>();
            window.titleContent = new GUIContent("Texture Packs");
            window.minSize = new Vector2(390f, 260f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Texture Packs");
            minSize = new Vector2(390f, 260f);
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            selectedPack = MinecraftTexturePackCatalog.ActivePack;
            RefreshValidation();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope(
                       GUILayout.ExpandWidth(true),
                       GUILayout.ExpandHeight(true)))
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Minecraft Texture Packs", titleStyle);
                EditorGUILayout.HelpBox(
                    "The selected pack is baked into the existing atlas in the Editor. " +
                    "There is no runtime texture-pack switch or player-facing setting.",
                    MessageType.Info);

                EditorGUI.BeginChangeCheck();
                selectedPack = (MinecraftTexturePackId)EditorGUILayout.Popup(
                    "Texture Pack",
                    (int)selectedPack,
                    PackLabels);
                if (EditorGUI.EndChangeCheck())
                    RefreshValidation();

                EditorGUILayout.LabelField(
                    "Currently Applied",
                    MinecraftTexturePackCatalog.DisplayName(MinecraftTexturePackCatalog.ActivePack));

                EditorGUILayout.HelpBox(
                    validationMessage,
                    selectedPackIsValid ? MessageType.Info : MessageType.Warning);

                if (selectedPack == MinecraftTexturePackId.Rodrigo)
                    DrawRodrigoControls();

                GUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(!selectedPackIsValid))
                {
                    if (GUILayout.Button("Apply Selected Texture Pack", GUILayout.Height(30f)))
                        ApplySelectedPack();
                }

                EditorGUILayout.HelpBox(
                    "Rodrigo's Pack is All Rights Reserved. Its ZIP stays under the ignored " +
                    "LocalTexturePacks folder and must not be committed or redistributed.",
                    MessageType.Warning);
            }
        }

        private void DrawRodrigoControls()
        {
            EditorGUILayout.LabelField("Local ZIP");
            EditorGUILayout.SelectableLabel(
                MinecraftTexturePackCatalog.RodrigoZipPath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Download / Repair Official ZIP"))
                    DownloadRodrigo();
                using (new EditorGUI.DisabledScope(
                           !MinecraftTexturePackCatalog.IsInstalled(MinecraftTexturePackId.Rodrigo)))
                {
                    if (GUILayout.Button("Reveal ZIP"))
                        EditorUtility.RevealInFinder(MinecraftTexturePackCatalog.RodrigoZipPath);
                }
            }
        }

        private void DownloadRodrigo()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Minecraft Texture Packs",
                    "Downloading Rodrigo's Pack from official Modrinth CDN...",
                    0.5f);
                MinecraftTexturePackCatalog.DownloadRodrigo();
                RefreshValidation();
                Debug.Log("Downloaded and verified Rodrigo's Pack for local Editor-only use.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Texture Pack Download Failed",
                    exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ApplySelectedPack()
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    "Minecraft Texture Packs",
                    "Rebuilding atlas and render manifest...",
                    0.5f);
                var result = MinecraftBlockPrefabGenerator.ApplyTexturePack(selectedPack);
                RefreshValidation();
                Repaint();
                Debug.Log(
                    $"Applied {MinecraftTexturePackCatalog.DisplayName(selectedPack)}: " +
                    $"{result.texturePackOverrides} overrides, " +
                    $"{result.texturePackFallbacks} Minecraft-default fallbacks.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Texture Pack Apply Failed",
                    exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void RefreshValidation()
        {
            selectedPackIsValid = MinecraftTexturePackCatalog.ValidateInstalledPack(
                selectedPack,
                out validationMessage);
        }
    }
}
