// Copyright Elliot Bentine, 2018-
using System;
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
    public class ProPixelizerApplyPixelizationMapToDepth : ProPixelizerApplyPixelizationMapPass, ExposesTargetForProPixelizerBlitPass
    {
        protected override string ProfilingName => nameof(ProPixelizerApplyPixelizationMapToDepth);

        public ProPixelizerApplyPixelizationMapToDepth(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configPass,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerPixelizationMapPass mapPass)
            : base(shaders, metadata, configPass, targetPass, mapPass)
        {
        }

        protected override bool OutputsDepth() => true;

        #region non-RG
#if !PRERG_API_REMOVED
        public RTHandle GetTargetForBlitPassNonRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType) {
            if (sourceType != ProPixelizerBlitPass.ProPixelizerRTHandleType.Depth)
                throw new ArgumentException("sourceType");
            return _Expanded;
        }

        /// <summary>
        /// Pixelization-mapped depth expressed in the bordered low-resolution target space.
        /// </summary>
        public RTHandle PixelizationMappedDepthInLowResSpace => _Expanded;

        protected override RenderTextureDescriptor GetOutputTextureDescriptor(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.useMipMap = false;
            if (ConfigPass.Data.Width > 0)
            {
                cameraTextureDescriptor.width = ConfigPass.Data.Width;
                cameraTextureDescriptor.height = ConfigPass.Data.Height;
            }

            var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;
            return depthDescriptor;
        }

        protected override RTHandle NonRGGetSourceRT(ref RenderingData renderingData)
        {
            if (Settings.TargetForPixelation == TargetForPixelation.CameraTarget)
                return NonRGGetSceneDepth(ref renderingData);
            return TargetPass.Depth;
        }

        protected override RTHandle NonRGGetSceneDepthRT(ref RenderingData renderingData)
        {
            return Settings.UsingRedirection && Settings.PrepassConfiguration == PipelinePrepassConfiguration.None ? TargetPass.Depth : NonRGGetSceneDepth(ref renderingData);
        }
#endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        TextureDesc GetTargetDescriptor(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lowResConfig = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();
            return lowResTarget.DepthDescriptor(resources.cameraDepth.GetDescriptor(renderGraph), lowResConfig);
        }

        protected override TextureHandle GetSourceRG(ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();

            return Settings.TargetForPixelation == TargetForPixelation.CameraTarget
                ? GetSceneDepthTexture(resources)
                : lowResTarget.Depth;
        }

        protected override TextureHandle GetExpandedRG(RenderGraph renderGraph, ContextContainer frameData)
        {
            var targetDescriptor = GetTargetDescriptor(renderGraph, frameData);
            targetDescriptor.clearBuffer = true;
            targetDescriptor.filterMode = FilterMode.Point;
            targetDescriptor.wrapMode = TextureWrapMode.Clamp;
            targetDescriptor.name = "ProPixelizer_PixelExpanded_Depth";
            var pixelExpandedTarget = frameData.GetOrCreate<LowResPixelExpanded>();
            pixelExpandedTarget.Depth = renderGraph.CreateTexture(targetDescriptor);
            return pixelExpandedTarget.Depth;
        }

        protected override TextureHandle GetPointFilteredSourceRG(RenderGraph renderGraph, ContextContainer frameData)
        {
            // depth never resamples
            throw new NotImplementedException();
        }

        protected override TextureHandle GetUnpixelizedSceneDepthRG(ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();

            return Settings.UsingRedirection && !Settings.UsingDepthPrepass
                ? lowResTarget.Depth
                : GetSceneDepthTexture(resources);
        }

        public TextureHandle GetTargetForBlitPassRG(
            ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType,
            ContextContainer context
            ) => context.Get<LowResPixelExpanded>().Depth;
#endif
        #endregion
    }
}
