// Copyright Elliot Bentine, 2018-
using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using ProPixelizer.Tools.Migration;
using static Unity.Rendering.Universal.ShaderUtils;

namespace ProPixelizer.Universal.ShaderGraph
{
    class ProPixelizerShaderGraphGUI : BaseShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditorIn, MaterialProperty[] properties)
        {
            EditorGUILayout.LabelField("ProPixelizer | ShaderGraph SubTarget", EditorStyles.boldLabel);
            if (GUILayout.Button("User Guide")) Application.OpenURL(ProPixelizerURPConstants.USER_GUIDE_URL);
            EditorGUILayout.Space();

            materialEditorIn.serializedObject.Update();
            if (CheckForUpdate(materialEditorIn.serializedObject))
                return;

            using (var header = new MaterialHeaderScope("ProPixelizer Properties", 1 << 12, materialEditorIn))
            {
                if (header.expanded)
                    DrawProPixelizerProperties(materialEditorIn, properties);
            }
            base.OnGUI(materialEditorIn, properties);
        }

        public void DrawProPixelizerProperties(MaterialEditor editor, MaterialProperty[] properties)
        {
            var material = (Material)editor.target;
            if (material == null)
                return;

            EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var colorGrading = FindProperty(ProPixelizerMaterialPropertyLabels.COLOR_GRADING, properties);
            editor.ShaderProperty(colorGrading, "Use Color Grading");
            if (colorGrading.floatValue > 0f)
            {
                var lut = FindProperty(ProPixelizerMaterialPropertyReferences.PaletteLUT, properties);
                editor.TextureProperty(lut, "Palette", false);
            }

            var dithering = FindProperty(ProPixelizerMaterialKeywordStrings.PROPIXELIZER_DITHERING, properties);
            editor.ShaderProperty(dithering, "Use Dithering");
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var ramp = FindProperty(ProPixelizerMaterialPropertyReferences.LightingRamp, properties);
            var lightRampTexture = editor.TextureProperty(ramp, "Lighting Ramp", false);
            if (lightRampTexture != null && lightRampTexture.mipmapCount > 1)
                EditorGUILayout.HelpBox("The selected light ramp texture has mipmaps enabled; this is not recommended as it can produce artefacts. Please disable in the Advanced settings of your texture.", MessageType.Warning);

            var lightingRampRange = FindProperty(ProPixelizerMaterialPropertyReferences.LightingRampRange, properties);
            editor.ShaderProperty(lightingRampRange, "Lighting Ramp Range");

            var shadowTintColor = FindProperty(ProPixelizerMaterialPropertyReferences.ShadowTint, properties);
            editor.ShaderProperty(shadowTintColor, "Shadow Tint");

            var ambient = FindProperty(ProPixelizerMaterialPropertyReferences.AmbientLight, properties);
            EditorGUILayout.LabelField("Ambient Light", EditorStyles.label);
            EditorGUI.BeginChangeCheck();
            bool hdr = (ambient.flags & MaterialProperty.PropFlags.HDR) != 0;
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("The ambient light value can be blended between a constant color value (weight 0) and the scene ambient light color (weight 1). The scene ambient light color includes contributions from spherical harmonics and baked lighting.", MessageType.None);
            Rect ambientColorRect = EditorGUILayout.GetControlRect(true, 18f, EditorStyles.layerMaskField);
            Rect ambientAlphaRect = EditorGUILayout.GetControlRect(true, 18f, EditorStyles.layerMaskField);
            Color ambientColor = EditorGUI.ColorField(ambientColorRect, new GUIContent("Constant Color"), ambient.colorValue, true, false, hdr);
            float ambientAlpha = EditorGUI.Slider(ambientAlphaRect, new GUIContent("Constant Weight"), ambient.colorValue.a, 0f, 1f);
            
            EditorGUI.indentLevel--;
            if (EditorGUI.EndChangeCheck())
            {
                ambientColor.a = ambientAlpha;
                ambient.colorValue = ambientColor;
            }
            

            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("Pixelization", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var pixelSize = FindProperty(ProPixelizerMaterialPropertyReferences.PixelSize, properties);
            editor.ShaderProperty(pixelSize, "Pixel Size");
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Note: Pixel Size does not affect preview camera views.", EditorStyles.wordWrappedMiniLabel);
            EditorGUI.indentLevel--;
            if (material.IsKeywordEnabled(ProPixelizerMaterialKeywordStrings.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON))
            {
                EditorGUILayout.HelpBox("Keyword " + ProPixelizerMaterialKeywordStrings.USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON + " is enabled.", MessageType.None);
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("Outlining", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            var oID = FindProperty(ProPixelizerMaterialPropertyReferences.ID, properties);
            editor.ShaderProperty(oID, ProPixelizerMaterialPropertyLabels.ID);
            EditorGUILayout.HelpBox("The ID is an 8-bit number used to identify different objects in the " +
                "buffer for purposes of drawing outlines. Outlines are drawn when a pixel is next to a pixel " +
                "of different ID value.", MessageType.None);
            EditorGUI.indentLevel--;
        }

        public MaterialProperty workflowMode;

        MaterialProperty[] properties;

        // collect properties from the material properties
        public override void FindProperties(MaterialProperty[] properties)
        {
            // save off the list of all properties for shadergraph
            this.properties = properties;

            var material = materialEditor?.target as Material;
            if (material == null)
                return;

            base.FindProperties(properties);
            workflowMode = FindProperty(Property.SpecularWorkflowMode, properties, false);
        }

        public override void DrawSurfaceOptions(Material material)
        {
            // Use default labelWidth
            EditorGUIUtility.labelWidth = 0f;
            if (workflowMode != null)
                DoPopup(LitGUI.Styles.workflowModeText, workflowMode, Enum.GetNames(typeof(LitGUI.WorkflowMode)));
            base.DrawSurfaceOptions(material);
        }

        public override void DrawSurfaceInputs(Material material)
        {
#if UNITY_6000_6_OR_NEWER
            DrawShaderGraphProperties(properties);
#else
            DrawShaderGraphProperties(material, properties);
#endif
        }

        public static void UpdateMaterial(Material material, MaterialUpdateType updateType)
        {
            // newly created materials should initialize the globalIlluminationFlags (default is off)
            if (updateType == MaterialUpdateType.CreatedNewMaterial)
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                ProPixelizerMaterialVersion.SetProPixelizerMaterialVersion(
                    material,
                    ProPixelizer2_0b1UberMaterialUpdater.VERSION);
            }

            bool automaticRenderQueue = GetAutomaticQueueControlSetting(material);
            BaseShaderGUI.UpdateMaterialSurfaceOptions(material, automaticRenderQueue);
            LitGUI.SetupSpecularWorkflowKeyword(material, out bool isSpecularWorkflow);
        }

        public override void ValidateMaterial(Material material)
        {
            if (material == null)
                throw new ArgumentNullException("material");

            UpdateMaterial(material, MaterialUpdateType.ModifiedMaterial);
        }

        private bool CheckForUpdate(SerializedObject so)
        {
            try
            {
                var updater = new ProPixelizer2_0b1UberMaterialUpdater();
                if (updater.CheckForUpdate(so))
                {
                    EditorGUILayout.HelpBox(
                        "Previous version of a ProPixelizer Shader Graph material detected.",
                        MessageType.Warning);
                    if (GUILayout.Button("Update"))
                        updater.DoUpdate(so, true);
                    EditorGUILayout.Space();
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorGUILayout.HelpBox(
                    "An error occurred while checking whether this material requires migration.",
                    MessageType.Warning);
            }
            return false;
        }

        public override void DrawAdvancedOptions(Material material)
        {
            // Always show the queue control field.  Only show the render queue field if queue control is set to user override
            //DoPopup(Styles.queueControl, queueControlProp, Styles.queueControlNames);
            //if (material.HasProperty(Property.QueueControl) && material.GetFloat(Property.QueueControl) == (float)QueueControl.UserOverride)
            //    materialEditor.RenderQueueField();
            base.DrawAdvancedOptions(material);

            // ignore emission color for shadergraphs, because shadergraphs don't have a hard-coded emission property, it's up to the user
            materialEditor.DoubleSidedGIField();
            materialEditor.LightmapEmissionFlagsProperty(0, enabled: true, ignoreEmissionColor: true);
        }
    }
}
