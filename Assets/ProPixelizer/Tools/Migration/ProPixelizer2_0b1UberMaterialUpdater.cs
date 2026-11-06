// Copyright Elliot Bentine, 2022-
#if UNITY_EDITOR
using System.Collections.Generic;
using ProPixelizer.Universal.ShaderGraph;
using UnityEditor;
using UnityEngine;

namespace ProPixelizer.Tools.Migration
{
    /// <summary>
    /// Updates materials using the ProPixelizer Uber shader to the latest material version.
    /// </summary>
    public class ProPixelizer2_0b1UberMaterialUpdater : ProPixelizerMaterialUpdater
    {
        public static readonly int VERSION = 2002;
        private const string UniversalRenderPipelineAsset =
            "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";

        public override List<IMigratedProperty> GetMigratedProperties()
        {
            return new List<IMigratedProperty>
            {
                new MigratedWorkflow()
            };
        }

        public class MigratedWorkflow : IMigratedProperty
        {
            private const string WorkflowMode = "_WorkflowMode";
            private const string SpecularSetupKeyword = "_SPECULAR_SETUP";
            private const string SpecularColor = "_SpecColor";
            private const string Metallic = "_Metallic";
            private const string Smoothness = "_Smoothness";
            private const string SpecularMap = "_SpecGlossMap";
            private const string MetallicMap = "_MetallicGlossMap";
            private const string NormalMap = "_BumpMap";

            public bool CheckForUpdate(SerializedObject so)
            {
                if (!IsProPixelizerShaderGraphMaterial(so))
                    return false;

                return !ProPixelizerMaterialVersion.TryGetProPixelizerMaterialVersion(
                           so,
                           out int version) ||
                       version < VERSION;
            }

            public void DoUpdate(SerializedObject so)
            {
                if (!CheckForUpdate(so))
                    return;

                so.ApplyModifiedPropertiesWithoutUndo();
                if (so.targetObject is Material material)
                {
                    ApplyLegacyAppearance(material, clearNormalMap: true);
                    EditorUtility.SetDirty(material);
                }
                so.Update();
            }

            public static void ApplyLegacyAppearance(Material material, bool clearNormalMap)
            {
                // These properties existed on Unity Lit materials but were previously ignored by
                // the ProPixelizer Uber shader. Reset them so upgrading does not alter appearance.
                material.SetFloat(WorkflowMode, 0.0f);
                material.EnableKeyword(SpecularSetupKeyword);
                material.SetColor(SpecularColor, Color.black);
                material.SetFloat(Metallic, 0.0f);
                material.SetFloat(Smoothness, 0.0f);
                material.SetTexture(SpecularMap, null);
                material.SetTexture(MetallicMap, null);
                if (clearNormalMap)
                {
                    // Normal maps were already supported by PixelizedWithOutline, but were ignored
                    // by older Uber shaders. Only clear them when migrating an old Uber material.
                    material.SetTexture(NormalMap, null);
                }
                material.SetColor(
                    ProPixelizer.Universal.ShaderGraph.ProPixelizerMaterialPropertyReferences.ShadowTint,
                    new Color(0f, 0f, 0f, 0f));
                ProPixelizerMaterialVersion.SetProPixelizerMaterialVersion(material, VERSION);
            }

            private static bool IsProPixelizerShaderGraphMaterial(SerializedObject so)
            {
                if (!(so.targetObject is Material material) || material.shader == null)
                    return false;

                return string.Equals(
                    ShaderUtil.GetCustomEditorForRenderPipeline(
                        material.shader,
                        UniversalRenderPipelineAsset),
                    typeof(ProPixelizerShaderGraphGUI).FullName,
                    System.StringComparison.Ordinal);
            }
        }
    }
}
#endif
