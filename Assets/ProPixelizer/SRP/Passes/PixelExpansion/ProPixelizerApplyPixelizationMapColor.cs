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
    public class ProPixelizerApplyPixelizationMapToColor : ProPixelizerApplyPixelizationMapPass, ExposesTargetForProPixelizerBlitPass
    {
        protected override string ProfilingName => nameof(ProPixelizerApplyPixelizationMapToColor);

        public ProPixelizerApplyPixelizationMapToColor(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configPass,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerPixelizationMapPass mapPass) 
            : base(shaders, metadata, configPass, targetPass, mapPass)
        {
        }

        protected override bool OutputsDepth() => false;

        #region non-RG
#if !PRERG_API_REMOVED
        public RTHandle GetTargetForBlitPassNonRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType) {
            if (sourceType != ProPixelizerBlitPass.ProPixelizerRTHandleType.Color)
                throw new ArgumentException("sourceType");
            return _Expanded;
        }

        protected override RenderTextureDescriptor GetOutputTextureDescriptor(RenderTextureDescriptor cameraTextureDescriptor)
        {
            cameraTextureDescriptor.useMipMap = false;
            if (ConfigPass.Data.Width > 0)
            {
                cameraTextureDescriptor.width = ConfigPass.Data.Width;
                cameraTextureDescriptor.height = ConfigPass.Data.Height;
            }
            cameraTextureDescriptor.depthBufferBits = 0;
            return cameraTextureDescriptor;
        }

        protected override RTHandle NonRGGetSourceRT(ref RenderingData renderingData)
        {
            return ColorTargetForPixelization(ref renderingData, TargetPass);
        }

        protected override RTHandle NonRGGetSceneDepthRT(ref RenderingData renderingData)
        {
            if (Settings.UsingRedirection)
            {
                return TargetPass.Depth;
            }
            return NonRGGetSceneDepth(ref renderingData);
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
            return lowResTarget.ColorDescriptor(resources.cameraColor.GetDescriptor(renderGraph), lowResConfig);
        }

        protected override TextureHandle GetSourceRG(ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();
            switch (Settings.TargetForPixelation)
            {
                default:
                    return lowResTarget.Color;
                case TargetForPixelation.CameraTarget:
                    return resources.cameraColor;
            }
        }

        protected override TextureHandle GetExpandedRG(RenderGraph renderGraph, ContextContainer frameData)
        {
            var targetDescriptor = GetTargetDescriptor(renderGraph, frameData);
            targetDescriptor.clearBuffer = true;
            targetDescriptor.filterMode = FilterMode.Point;
            targetDescriptor.wrapMode = TextureWrapMode.Clamp;
            targetDescriptor.name = "ProPixelizer_PixelExpanded_Color";
            var pixelExpandedTarget = frameData.GetOrCreate<LowResPixelExpanded>();
            pixelExpandedTarget.Color = renderGraph.CreateTexture(targetDescriptor);
            return pixelExpandedTarget.Color;
        }

        protected override TextureHandle GetPointFilteredSourceRG(RenderGraph renderGraph, ContextContainer frameData)
        {
            var targetDescriptor = GetTargetDescriptor(renderGraph, frameData);
            targetDescriptor.clearBuffer = true;
            targetDescriptor.filterMode = FilterMode.Point;
            targetDescriptor.wrapMode = TextureWrapMode.Clamp;
            targetDescriptor.name = "ProPixelizer_PointFilteredColorTarget";
            return renderGraph.CreateTexture(targetDescriptor);
        }

        protected override TextureHandle GetUnpixelizedSceneDepthRG(ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();
            switch (Settings.TargetForPixelation)
            {
                default:
                    return lowResTarget.Depth;
                case TargetForPixelation.CameraTarget:
                    return GetSceneDepthTexture(resources);
            }
        }

        public TextureHandle GetTargetForBlitPassRG(
            ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType,
            ContextContainer context
            ) => context.Get<LowResPixelExpanded>().Color;
#endif
        #endregion
    }
}
