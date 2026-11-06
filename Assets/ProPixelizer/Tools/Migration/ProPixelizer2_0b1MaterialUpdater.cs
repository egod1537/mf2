// Copyright Elliot Bentine, 2022-
#if UNITY_EDITOR
using System.Collections.Generic;
using ProPixelizer.Universal.ShaderGraph;
using UnityEditor;
using UnityEngine;

namespace ProPixelizer.Tools.Migration
{
    public class ProPixelizer2_0b1MaterialUpdater : ProPixelizer2_0MaterialUpdater
    {
        public override List<IMigratedProperty> GetMigratedProperties()
        {
            var list = base.GetMigratedProperties();
            list.Add(new MigratedPixelizedWithOutlineToUber());
            return list;
        }

        public class MigratedPixelizedWithOutlineToUber : IMigratedProperty
        {
            public string UberShaderPath() =>
                AssetDatabase.GUIDToAssetPath(ProPixelizerShaderGUIDs.UBER_SHADER_GUID);

            public bool CheckForUpdate(SerializedObject so)
            {
                var shaderProperty = so.FindProperty("m_Shader");
                if (shaderProperty == null ||
                    shaderProperty.propertyType != SerializedPropertyType.ObjectReference ||
                    shaderProperty.objectReferenceValue == null)
                    return false;

                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    shaderProperty.objectReferenceValue,
                    out string guid,
                    out long _))
                    return false;

                return string.Equals(
                           guid,
                           ProPixelizerShaderGUIDs.PIXELIZED_WITH_OUTLINE_GUID,
                           System.StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(
                           guid,
                           ProPixelizerShaderGUIDs.PIXELIZED_WITH_OUTLINE_GUID_UPM_BRANCH,
                           System.StringComparison.OrdinalIgnoreCase);
            }

            public void DoUpdate(SerializedObject so)
            {
                var uberShader = AssetDatabase.LoadAssetAtPath<Shader>(UberShaderPath());
                if (uberShader == null)
                {
                    Debug.LogWarning("Cannot find uber shader");
                    return;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                foreach (var target in so.targetObjects)
                {
                    if (!(target is Material material))
                        continue;

                    material.shader = uberShader;
                    ProPixelizer2_0b1UberMaterialUpdater.MigratedWorkflow.ApplyLegacyAppearance(
                        material,
                        clearNormalMap: false);
                    EditorUtility.SetDirty(material);
                }
                so.Update();
            }
        }
    }
}
#endif
