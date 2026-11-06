// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
#if UNITY_2023_3_OR_NEWER
    /// <summary>
    /// Redirects draw calls 
    /// </summary>
    public class ProPixelizerStoreOriginalCameraTargetsPass : ProPixelizerPass
    {
        public ProPixelizerStoreOriginalCameraTargetsPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var originals = frameData.GetOrCreate<ProPixelizerOriginalCameraTargets>();

            originals.Color = resources.cameraColor;
            originals.Depth = resources.cameraDepth;
        }


    }
#endif
}