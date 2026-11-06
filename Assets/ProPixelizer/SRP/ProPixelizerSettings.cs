// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    public struct ProPixelizerSettings
    {
        public PixelizationFilter Filter;
        public LayerMask PixelizedLayers;
        public bool UsePixelExpansion;
        /// <summary>
        /// The resolution scaling of the low-resolution target, relative to the screen. Lower values = greater pixelisation.
        /// </summary>
        public float DefaultLowResScale;
        public bool UseDepthTestingForEdgeOutlines;
        public bool UseNormalsForEdgeDetection;
        public float DepthTestThreshold;
        public bool UseDepthTestingForIDOutlines;
        public float NormalEdgeDetectionThreshold;
        public Texture2D FullscreenLUT;
        public bool UsePixelArtUpscalingFilter;
        public bool Enabled;
        public ProPixelizerRenderingPathway RenderingPathway;
        public PipelinePrepassConfiguration PrepassConfiguration;

        public RecompositionLowResSource RecompositionSource =>
            RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera || RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                ? RecompositionLowResSource.ProPixelizerTargets
                : RecompositionLowResSource.SubCamera;
        public RecompositionMode RecompositionMode
        {
            get
            {
                if (RenderingPathway == ProPixelizerRenderingPathway.SubCamera)
                    return RecompositionMode.None;
                if (Filter == PixelizationFilter.FullScene || RenderingPathway == ProPixelizerRenderingPathway.MainCamera)
                    return RecompositionMode.UseLowRes;
                return RecompositionMode.DepthBasedMerge;
            }
        }

        /// <summary>
        /// The target into which ProPixelizer shaders are rendered and pixel expanded.
        /// </summary>
        public TargetForPixelation TargetForPixelation =>
            RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                ? TargetForPixelation.CameraTarget
                : TargetForPixelation.ProPixelizerTarget;

        public RenderPassEvent RecompositeColorEvent =>
            RenderingPathway == ProPixelizerRenderingPathway.MainCamera
                ? RenderPassEvent.BeforeRenderingOpaques
                : (Filter == PixelizationFilter.FullScene
                    ? RenderPassEvent.AfterRenderingTransparents
                    : RenderPassEvent.AfterRenderingOpaques);

        public RenderPassEvent ColorGradingEvent => RecompositeColorEvent;

        public RenderPassEvent RecompositeDepthEvent => 
            PrepassConfiguration == PipelinePrepassConfiguration.None 
                ? (RenderingPathway == ProPixelizerRenderingPathway.MainCamera ? RenderPassEvent.BeforeRenderingOpaques : RenderPassEvent.AfterRenderingOpaques) 
                : ApplyPixelizationToDepthAttachmentEvent;

        public RenderPassEvent ApplyPixelizationToDepthAttachmentEvent =>
            PrepassConfiguration == PipelinePrepassConfiguration.None ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingPrePasses;

        public bool ShouldBlitExpandedDepth =>
            !(RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera && Filter == PixelizationFilter.OnlyProPixelizer);

        /// <summary>
        /// Opaques should be drawn using the un-expanded depth, otherwise occlusion is too eager and we get missing pixels when objects overlap.
        /// </summary>
        public RenderPassEvent BlitExpandedDepthEvent => UsingRedirection ? RenderPassEvent.AfterRenderingOpaques : ApplyPixelizationToDepthAttachmentEvent;

        public bool ClearLowResolutionTargetBeforeDraw => (RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera && Filter == PixelizationFilter.OnlyProPixelizer);

        public RenderPassEvent ApplyPixelizationToColorEvent => RenderPassEvent.AfterRenderingOpaques;

        public RecomposedDepthAttachmentSourceMode GetRecomposedDepthAttachmentSource(bool usingRenderGraphPathway) =>
            (!usingRenderGraphPathway && UsingRedirection && UsePixelExpansion)
                ? RecomposedDepthAttachmentSourceMode.PixelExpandedDepth
                : RecomposedDepthAttachmentSourceMode.RecompositedDepth;

        public RenderPassEvent PixelizationMapEvent => PrepassConfiguration == PipelinePrepassConfiguration.None ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingPrePasses;

        public RenderPassEvent CopyRecomposedColorEvent => RecompositeColorEvent;

        public RenderPassEvent CopyRecomposedDepthTargetEvent =>
            RenderingPathway == ProPixelizerRenderingPathway.MainCamera 
                ? RenderPassEvent.BeforeRenderingOpaques // main camera does not apply pixelization to depth.
                : RecompositeDepthEvent;

        public RenderPassEvent CopyRecomposedDepthAttachmentEvent =>
    RenderingPathway == ProPixelizerRenderingPathway.MainCamera
        ? RenderPassEvent.BeforeRenderingOpaques // main camera does not apply pixelization to depth.
        : RenderPassEvent.AfterRenderingOpaques; // DepthAttachment is recreated from scratch, so we need to repopulate it.

        /// <summary>
        /// Returns true if the original scene depth must be stored.
        /// 
        /// This occurs when we recomposite depth before color. Depth-based recomposition for color requires the original scene depth to be available, but it isn't if we already recomposed depth.
        /// </summary>
        public bool IsStoringOriginalSceneDepthRequired =>
            RecompositionMode == RecompositionMode.DepthBasedMerge && 
            RecompositeDepthEvent < RecompositeColorEvent;

        /// <summary>
        /// True if using redirection to low-res target.
        /// </summary>
        public bool UsingRedirection => RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera && Filter == PixelizationFilter.FullScene;

        public bool UsingDepthPrepass => PrepassConfiguration != PipelinePrepassConfiguration.None;
    }

    public enum PixelizationFilter
    {
        OnlyProPixelizer,
        FullScene,
        [InspectorName(null)] Layers
    }

    public enum ProPixelizerRenderingPathway
    {
        MainCamera,
        SubCamera,
        MainWithVirtualCamera
    }

    public enum RecomposedDepthAttachmentTargetMode
    {
        LowResDepth,
        CameraDepthAttachment
    }

    public enum RecomposedDepthAttachmentSourceMode
    {
        RecompositedDepth,
        PixelExpandedDepth
    }

    /// <summary>
    /// ProPixelizer's approach for merging a low-resolution target back into the screen.
    /// </summary>
    public enum RecompositionMode
    {
        /// <summary>
        /// No recomposition, everything should be done directly into scene.
        /// </summary>
        None,
        /// <summary>
        /// Use the low resolution target in entirety.
        /// </summary>
        UseLowRes,
        /// <summary>
        /// Low-res target is merged based on comparisons of depth buffer.
        /// </summary>
        DepthBasedMerge
    }

    /// <summary>
    /// The source used for recompositing the low resolution scene.
    /// </summary>
    public enum RecompositionLowResSource
    {
        ProPixelizerTargets,
        SubCamera
    }

    public enum TargetForPixelation
    {
        CameraTarget,
        ProPixelizerTarget
    }

    public enum PipelinePrepassConfiguration
    {
        [Tooltip("Attempts to automatically determine prepass configuration.")]
        AutoDetect,
        None,
        Depth,
        DepthNormals
    }
}
