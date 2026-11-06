// Copyright Elliot Bentine, 2018-
#if UNITY_EDITOR
using ProPixelizer.Tools.Migration;
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

namespace ProPixelizer
{
    public class PixelizedWithOutlineShaderGUI : ShaderGUI
    {
        bool showColor, showPixelize, showLighting, showOutline;

        public virtual string DisplayName => "ProPixelizer | Appearance+Outline Material";

        Material Material;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            materialEditor.serializedObject.Update();
            Material = materialEditor.target as Material;

            EditorGUILayout.LabelField(DisplayName, EditorStyles.boldLabel);
            if (GUILayout.Button("User Guide")) Application.OpenURL(ProPixelizerUtils.USER_GUIDE_URL);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "The PixelizedWithOutline shader is now deprecated and replaced with the new UberShader from ProPixelizer v2.0 onwards. Please click Update to automatically port your material.",
            MessageType.Warning);
            if (CheckForUpdate(materialEditor.serializedObject))
                return;

            DrawAppearanceGroup(materialEditor, properties);
            DrawLightingGroup(materialEditor, properties);
            DrawPixelizeGroup(materialEditor, properties);
            //DrawAlphaGroup(materialEditor, properties);
            DrawOutlineGroup(materialEditor, properties);

            EditorGUILayout.Space();
            EditorGUILayout.Space();

            //var enableInstancing = EditorGUILayout.ToggleLeft("Enable GPU Instancing", Material.enableInstancing);
            //Material.enableInstancing = enableInstancing;
            Material.enableInstancing = false;
            var renderQueue = EditorGUILayout.IntField("Render Queue", Material.renderQueue);
            Material.renderQueue = renderQueue;
            var dsgi = EditorGUILayout.ToggleLeft("Double Sided Global Illumination", Material.doubleSidedGI);
            Material.doubleSidedGI = dsgi;

            EditorGUILayout.Space();
            EditorGUILayout.Space();
#if UNITY_2023_3_OR_NEWER
            if (!ProPixelizerUtils.PipelineUsingRG())
                GUILayout.TextArea("Note: Pixelization disabled in the Inspector preview window on Unity 2023 when (non-RenderGraph) compatibility mode is enabled.", EditorStyles.wordWrappedMiniLabel);
#else
            GUILayout.TextArea("Note: Pixelization disabled in the Inspector preview window on Unity 2022.", EditorStyles.wordWrappedMiniLabel);
#endif

            //EditorUtility.SetDirty(Material);
            materialEditor.serializedObject.ApplyModifiedProperties();
        }

        public void DrawAppearanceGroup(MaterialEditor editor, MaterialProperty[] properties)
        {
            showColor = EditorGUILayout.BeginFoldoutHeaderGroup(showColor, "Appearance");
            if (showColor)
            {
                var albedo = FindProperty("_BaseMap", properties);
                editor.TextureProperty(albedo, "Albedo", true);

                var baseColor = FindProperty("_BaseColor", properties);
                editor.ColorProperty(baseColor, "Color");

                var colorGrading = FindProperty(ProPixelizerKeywords.COLOR_GRADING, properties);
                editor.ShaderProperty(colorGrading, "Use Color Grading");
                if (colorGrading.floatValue > 0f)
                {
                    var lut = FindProperty("_PaletteLUT", properties);
                    editor.TextureProperty(lut, "Palette", false);
                }

                var normal = FindProperty("_BumpMap", properties);
                editor.TextureProperty(normal, "Normal Map", true);

                var emission = FindProperty("_EmissionMap", properties);
                editor.TextureProperty(emission, "Emission", true);

                var emissiveColor = FindProperty("_EmissionColor", properties);
                editor.ColorProperty(emissiveColor, "Emission Color");
                EditorGUILayout.HelpBox("Emission Color is multiplied by the Emission texture to determine the emissive output. The default emissive color and texture and both black.", MessageType.None);

                var diffuseVertexColorWeight = FindProperty("_DiffuseVertexColorWeight", properties);
                editor.RangeProperty(diffuseVertexColorWeight, "Diffuse Vertex Color Weight");
                var emissiveVertexColorWeight = FindProperty("_EmissiveVertexColorWeight", properties);
                editor.RangeProperty(emissiveVertexColorWeight, "Emissive Vertex Color Weight");
                EditorGUILayout.HelpBox("The vertex color sliders control how the emissive and diffuse colors are affected by a mesh's vertex colors. Vertex colors are used for many per-particle color properties by Unity's particle system.", MessageType.None);

                var use_alpha_clip = FindProperty(ProPixelizerKeywords.PROPIXELIZER_DITHERING, properties);
                editor.ShaderProperty(use_alpha_clip, "Use Dithered Transparency");
                var threshold = FindProperty("_AlphaClipThreshold", properties);
                editor.ShaderProperty(threshold, "Alpha Clip Threshold");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public void DrawLightingGroup(MaterialEditor editor, MaterialProperty[] properties)
        {
            showLighting = EditorGUILayout.BeginFoldoutHeaderGroup(showLighting, "Lighting");
            if (showLighting)
            {
                var ramp = FindProperty("_LightingRamp", properties);
                var lightRampTexture = editor.TextureProperty(ramp, "Lighting Ramp", false);
                if (lightRampTexture.mipmapCount > 1)
                    EditorGUILayout.HelpBox("The selected light ramp texture has mipmaps enabled; this is not recommended as it can produce artefacts. Please disable in the Advanced settings of your texture.", MessageType.Warning);

                var ambient = FindProperty("_AmbientLight", properties);
                editor.ShaderProperty(ambient, "Ambient Light");
                EditorGUILayout.HelpBox("The Ambient Light alpha value can be used to blend between scene ambient color and spherical harmonics (alpha zero) or the color of the Ambient Light property (alpha one).", MessageType.Info);

                var receiveShadows = FindProperty(ProPixelizerKeywords.RECEIVE_SHADOWS, properties);
                editor.ShaderProperty(receiveShadows, "Receive shadows");
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public void DrawPixelizeGroup(MaterialEditor editor, MaterialProperty[] properties)
        {
            showPixelize = EditorGUILayout.BeginFoldoutHeaderGroup(showPixelize, "Pixelize");
            if (showPixelize)
            {
                var pixelSize = FindProperty("_PixelSize", properties);
                editor.ShaderProperty(pixelSize, "Pixel Size");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Note: Pixel Size does not affect preview camera views.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
                if (Material.IsKeywordEnabled(ProPixelizerKeywords.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON))
                {
                    EditorGUILayout.HelpBox("Keyword " + ProPixelizerKeywords.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON + " is enabled.", MessageType.None);
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        public void DrawOutlineGroup(MaterialEditor editor, MaterialProperty[] properties)
        {
            showOutline = EditorGUILayout.BeginFoldoutHeaderGroup(showOutline, "Outline");
            if (showOutline)
            {
                var oID = FindProperty("_ID", properties);
                editor.ShaderProperty(oID, "ID");
                EditorGUILayout.HelpBox("The ID is an 8-bit number used to identify different objects in the " +
                    "buffer for purposes of drawing outlines. Outlines are drawn when a pixel is next to a pixel " +
                    "of different ID value.", MessageType.None);
                var outlineColor = FindProperty("_OutlineColor", properties);
                editor.ShaderProperty(outlineColor, "Outline Color");
                var edgeHighlightColor = FindProperty("_EdgeHighlightColor", properties);
                editor.ShaderProperty(edgeHighlightColor, "Edge Highlight");
                EditorGUILayout.HelpBox("Use color values less than 0.5 to darken edge highlights. " +
                    "Use color values greater than 0.5 to lighten edge highlights. " +
                    "Use color values of 0.5 to make edge highlights invisible.\n\n" +
                    "You may need to enable the setting in the Pixelisation Feature on your Forward Renderer asset.", MessageType.None);
                var bevelWeight = FindProperty("_EdgeBevelWeight", properties);
                editor.ShaderProperty(bevelWeight, "Edge Bevel Weight");
                EditorGUILayout.HelpBox("Controls the strength of edge bevelling between 0 (off) and 1 (fully on). When on, edges that are detected use the average normal from their neighbourhood.", MessageType.None);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>
        /// Checks if the serialized object needs to be updated, e.g. because properties changed between versions.
        /// </summary>
        /// <param name="so"></param>
        public bool CheckForUpdate(SerializedObject so)
        {
            try
            {
                var updater = new ProPixelizer2_0b1MaterialUpdater();
                if (updater.CheckForUpdate(so))
                {
                    EditorGUILayout.HelpBox(
                        "Properties from a previous version of ProPixelizer detected. " +
                        "Press the button below to migrate material properties to new names.",
                        MessageType.Warning);
                    if (GUILayout.Button("Update"))
                        updater.DoUpdate(so, true);
                    EditorGUILayout.Space();
                    return true;
                }
            }
            catch (Exception)
            {
                EditorGUILayout.HelpBox(
                "Error occured while attempting to check if material requires migration.",
                MessageType.Warning);
            }
            return false;
        }
    }
}
#endif
