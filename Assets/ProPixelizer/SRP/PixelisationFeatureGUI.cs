// Copyright Elliot Bentine, 2018-
#if (UNITY_EDITOR)

using UnityEditor;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;

namespace ProPixelizer.Tools
{
    [CustomEditor(typeof(ProPixelizerRenderFeature))]
    public class PixelisationFeatureGUI : Editor
    {
        public override void OnInspectorGUI()
        {
            ProPixelizerRenderFeature feature = (ProPixelizerRenderFeature)target;
            ProPixelizerVerification.CheckProPixelizerRenderFeatureList(
                feature,
                out bool proPixelizerIsLastActiveFeature,
                out int proPixelizerRenderFeatureCount
            );

            EditorGUILayout.HelpBox("This Render Feature adds all passes required for ProPixelizer.", MessageType.None);
            serializedObject.Update();

            if (proPixelizerRenderFeatureCount > 1)
            {
                EditorGUILayout.HelpBox(
                    "You currently have multiple active ProPixelizer Render Features. Only one is supported.", MessageType.Warning
                    );
            }

#if UNITY_6000_0_OR_NEWER
            if (ProPixelizerUtils.PipelineUsingPreRGPathway())
                EditorGUILayout.HelpBox(
                "Your project is currently rendering using Compatibility Mode (Project Settings > Graphics). " +
                "It is recommended that you disable Compatibility Mode to use the newer RenderGraph API.", MessageType.Warning
            );
#endif

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Pixelization", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UsePixelExpansion"));
            EditorGUILayout.HelpBox(
                "ProPixelizer's Pixel Expansion method allows you to have per-object pixel sizes. It also" +
                " allows pixelated objects to move relative to one another at increments smaller than their " +
                "pixel size, which can help motion feel smooth and responsive.",
                MessageType.None);
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("PixelizationFilter"));
            //switch (feature.PixelizationFilter)
            //{
            //    case PixelizationFilter.FullScene:
            //        EditorGUILayout.PropertyField(serializedObject.FindProperty("UsePixelArtUpscalingFilter"));
            //        break;
            //    case PixelizationFilter.Layers:
            //        EditorGUILayout.PropertyField(serializedObject.FindProperty("PixelizedLayers"));
            //        break;
            //}
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("DefaultLowResScale"));
            
            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Outline Detection Controls", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UseDepthTestingForIDOutlines"));
            if (feature.UseDepthTestingForIDOutlines)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("DepthTestThreshold"));            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("UseNormalsForEdgeDetection"));
            if (feature.UseNormalsForEdgeDetection)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("NormalEdgeDetectionThreshold"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("UseDepthTestingForEdgeOutlines"));
            }

            //EditorGUILayout.LabelField("");
            //EditorGUILayout.Separator();
            //EditorGUILayout.LabelField("Fullscreen Options", EditorStyles.boldLabel);
            //EditorGUILayout.HelpBox(
            //    "These settings are applied to the entire screen. ProPixelizer also supports per-object color grading" +
            //    " and edge control through individual materials. You may also override these properties for each camera.",
            //    MessageType.None);
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("FullscreenColorPalette"));

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("URP Renderer", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PrepassConfiguration"));
            //EditorGUILayout.EnumPopup("Auto value", ProPixelizerUtils.GuessPrepassConfiguration(feature.))
            if (feature.PrepassConfiguration == PipelinePrepassConfiguration.AutoDetect && !proPixelizerIsLastActiveFeature)
            {
                EditorGUILayout.HelpBox(
                    "Auto-prepass detection can only inspect other renderer features in the list when they are above ProPixelizer. " +
                    "Move ProPixelizer below all active renderer features, or select an explicit prepass configuration.",
                    MessageType.Warning);
            }

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Editor Scene Tab", EditorStyles.boldLabel);
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("EditorFullscreenColorGrading"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("EditorPixelExpansion"));

            EditorGUILayout.Separator();
            EditorGUILayout.LabelField("Debugging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("GenerateWarnings"));
            DrawAutoFix();

            serializedObject.ApplyModifiedProperties();
        }

        public void DrawAutoFix()
        {
            var rpa = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (ProPixelizerVerification.RPANeedsFix(rpa))
            {
                if (GUILayout.Button("Fix Current Render Pipeline Asset"))
                    ProPixelizerVerification.TryFixRPA(rpa);
            }
        }
    }
}

#endif