// Copyright Elliot Bentine, 2018-
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace ProPixelizer
{
    /// <summary>
    /// Provides some simple verifications to ensure that the asset is being used correctly,
    /// and provides messages to users to help them figure out when it is not.
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public class ProPixelizerVerification
    {
        static ProPixelizerVerification()
        {
#if UNITY_EDITOR
            //EditorApplication.delayCall += ReimportShaders;
#endif
        }

        /// <summary>
        /// Forces ProPixelizer shaders to be imported in the correct order.
        /// This is to fix a Unity issue with UsePass, where sometimes shaders do not correctly import passes from SG shaders if imported before them.
        /// </summary>
        public static void ReimportShaders()
        {
#if UNITY_EDITOR
            AssetDatabase.ImportAsset(Utils.PackageLocation + "/ShaderGraph/ProPixelizerBase.shadergraph", ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(Utils.PackageLocation + "/SRP/ShaderLibrary/PixelizedWithOutline.shader", ImportAssetOptions.ForceUpdate);
#endif
        }

        public static void GenerateWarnings()
        {
#if UNITY_EDITOR
            bool generatedWarning = false;
            // In the future, I hope that Unity makes ShaderGraphPreferences public so that I can use that rather than hard-coded names here.
            int variantLimit = EditorPrefs.GetInt("UnityEditor.ShaderGraph.VariantLimit", 128);
            if (variantLimit < 128)
            {
                Debug.LogWarning(string.Format(
                    "The ShaderGraph Variant Limit is currently set to a value of {0}. " +
                    "The ProPixelizer appearance shader will not compile unless this variant " +
                    "is raised, and your shaders will appear pink. Please increase this limit, " +
                    "e.g. to 128, by changing Preferences > ShaderGraph > Shader Variant Limit. " +
                    "Afterwards, reimport the ProPixelizer folder.",
                    variantLimit));
                generatedWarning = true;
            }

            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                if (asset.msaaSampleCount > 1)
                {
                    Debug.LogWarning(string.Format("MSAA is enabled in the active render pipeline asset, this is incompatible with ProPixelizer. " +
                        "To fix this, navigate to your Render Pipeline Asset and set Quality -> Anti Aliasing (MSAA) to Disabled."));
                    generatedWarning = true;
                }
            }

            if (generatedWarning)
                Debug.LogWarning("Warnings have been emitted during the ProPixelizer verification step. You can disable them by unticking 'Generate Warnings' in the ProPixelizer Render Feature.");
#endif
        }

#if UNITY_EDITOR
        public static bool RPANeedsFix(UniversalRenderPipelineAsset rpa)
        {
            if (rpa.msaaSampleCount > 1)
                return true;
            return false;
        }

        public static void TryFixRPA(UniversalRenderPipelineAsset rpa)
        {
            rpa.msaaSampleCount = 1;
        }

        public static void CheckProPixelizerRenderFeatureList(ProPixelizerRenderFeature feature, out bool proPixelizerIsLastActive, out int proPixelizerRenderFeatureCount)
        {
            var assetPath = AssetDatabase.GetAssetPath(feature);
            var data = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<ScriptableRendererData>().FirstOrDefault(data => data.rendererFeatures.Contains(feature));

            proPixelizerIsLastActive = false;
            proPixelizerRenderFeatureCount = 0;

            foreach (var feat in data.rendererFeatures)
            {
                if (!feat.isActive) continue;

                if (feat is ProPixelizerRenderFeature)
                {
                    proPixelizerIsLastActive = true;
                    proPixelizerRenderFeatureCount++;
                }
                else
                    proPixelizerIsLastActive = false;
            }
        }
#endif
    }
}