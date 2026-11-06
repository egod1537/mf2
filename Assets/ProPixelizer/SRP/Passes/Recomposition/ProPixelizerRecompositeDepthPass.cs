// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    public class ProPixelizerRecompositeDepthPass : ProPixelizerRecompositePass, ExposesTargetForProPixelizerBlitPass
    {
        public ProPixelizerRecompositeDepthPass(
            ShaderResources resources,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerStoreSceneDepthPass originalSceneDepth,
            ProPixelizerMetadataPass metadataPass,
            ProPixelizerApplyPixelizationMapToDepth pixelExpandedDepthPass
            ) : base(
                resources,
                targetPass,
                originalSceneDepth,
                metadataPass,
                pixelExpandedDepthPass)
        {
        }

        protected override string ProfilingName => nameof(ProPixelizerRecompositeDepthPass);

        protected override RecompositionMode GetRecompositionMode()
        {
            if (Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera
                && Settings.PrepassConfiguration != PipelinePrepassConfiguration.None)
                return RecompositionMode.DepthBasedMerge; // prepasses are not redirected, so we must use depth based merge.
            return Settings.RecompositionMode;
        }

        protected override bool OutputsDepth() => true;
        protected override string GetRecompositionTargetName() => ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_DEPTH;
#if !PRERG_API_REMOVED
        protected override RTHandle GetLowResSourceForNonRGPath(ref RenderingData renderingData) => NonRGGetLowResDepth(ref renderingData);
        protected override RTHandle GetSceneSourceForNonRGPath(ref RenderingData renderingData) => NonRGGetSceneDepth(ref renderingData);
#endif
        protected override RenderTextureDescriptor GetRecompositionTargetDescriptor(in RenderTextureDescriptor cameraTextureDescriptor)
        {
            var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            return depthDescriptor;
        }

        #if UNITY_2023_3_OR_NEWER
        protected override TextureDesc GetOutputTextureDesc(RenderGraph renderGraph, ContextContainer frameData)
        { 
            var originalTargets = frameData.Get<ProPixelizerOriginalCameraTargets>();
            var depthDescriptor = originalTargets.Depth.GetDescriptor(renderGraph);
            depthDescriptor.name = ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_DEPTH;
            depthDescriptor.clearBuffer = false;
            return depthDescriptor;
        }

        protected override TextureHandle CreateRecomposedTextureHandle(RenderGraph renderGraph, ContextContainer frameData)
        {
            var recomposed = frameData.GetOrCreate<ProPixelizerRecomposedDepthData>();
            var descriptor = GetOutputTextureDesc(renderGraph, frameData);
            recomposed.Target = renderGraph.CreateTexture(descriptor);
            return recomposed.Target;
        }

        protected override TextureHandle GetLowResSource(ContextContainer frameData) => GetLowResDepth(frameData);

        protected override TextureHandle GetSceneSource(ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            return CameraDepthTexture(resources, cameraData);
        }

        public TextureHandle GetTargetForBlitPassRG(
            ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType,
            ContextContainer context
            ) => context.Get<ProPixelizerRecomposedDepthData>().Target;
#endif
    }
}
