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
    public class ProPixelizerRecompositeColorPass : ProPixelizerRecompositePass, ExposesTargetForProPixelizerBlitPass
    {
        public ProPixelizerRecompositeColorPass(
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
        protected override RecompositionMode GetRecompositionMode() => Settings.RecompositionMode;

        protected override bool OutputsDepth() => false;
        protected override string GetRecompositionTargetName() => ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_COLOR;
        protected override string ProfilingName => nameof(ProPixelizerRecompositeColorPass);

        protected override RenderTextureDescriptor GetRecompositionTargetDescriptor(in RenderTextureDescriptor cameraTextureDescriptor)
        {
            var colorDescriptor = cameraTextureDescriptor;
            colorDescriptor.depthBufferBits = 0;
            return colorDescriptor;
        }

        #if !PRERG_API_REMOVED
        protected override RTHandle GetLowResSourceForNonRGPath(ref RenderingData renderingData)
        {
            RTHandle sourceColor;
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerTargets:
                default:
                    sourceColor = TargetPass.Color;
                    break;
                case RecompositionLowResSource.SubCamera:
                    sourceColor = renderingData.cameraData.camera.GetComponent<ProPixelizerCamera>().SubCamera.Color;
                    break;
            }
            return sourceColor;
        }

        protected override RTHandle GetSceneSourceForNonRGPath(ref RenderingData renderingData) => ColorTarget(ref renderingData);
        #endif

        #if UNITY_2023_3_OR_NEWER

        protected override TextureDesc GetOutputTextureDesc(RenderGraph renderGraph, ContextContainer frameData)
        {
            var originalTargets = frameData.Get<ProPixelizerOriginalCameraTargets>();
            var colorDescriptor = originalTargets.Color.GetDescriptor(renderGraph);
            colorDescriptor.name = ProPixelizerTargets.PROPIXELIZER_RECOMPOSITION_COLOR;
            colorDescriptor.clearBuffer = false;
            return colorDescriptor;
        }

        protected override TextureHandle CreateRecomposedTextureHandle(RenderGraph renderGraph, ContextContainer frameData)
        {
            var recomposed = frameData.GetOrCreate<ProPixelizerRecomposedColorData>();
            var descriptor = GetOutputTextureDesc(renderGraph, frameData);
            recomposed.Target = renderGraph.CreateTexture(descriptor);
            return recomposed.Target;
        }

        protected override TextureHandle GetLowResSource(ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerTargets:
                default:
                    return lowResTarget.Color;
                case RecompositionLowResSource.SubCamera:
                    return frameData.Get<ProPixelizerSubCameraTargets>().Color;
            }
        }

        protected override TextureHandle GetSceneSource(ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            return CameraColorTexture(resources, cameraData);
        }

        public TextureHandle GetTargetForBlitPassRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType, ContextContainer context)
        {
            return context.Get<ProPixelizerRecomposedColorData>().Target;
        }
#endif
    }
}
