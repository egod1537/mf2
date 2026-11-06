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
    /// <summary>
    /// The recomposition pass is used to merge the low-resolution render of the scene with the high-resolution scene.
    /// The low-resolution texture can either be from a subcamera, or from a ProPixelizer low-resolution target.
    /// 
    /// Recomposition can be made by a few strategies. These include:
    /// - a depth-based merge by comparing the depth buffers of the two targets and selecting the values which are nearer.
    /// - one of the targets is taken completely. This is _not_ a blit, because we need to map the low-res target into the high
    ///   resolution scene to account for sub-pixel camera movement.
    /// </summary>
    public abstract class ProPixelizerRecompositePass : ProPixelizerPass
    {
        public ProPixelizerRecompositePass(
            ShaderResources resources,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerStoreSceneDepthPass originalSceneDepth,
            ProPixelizerMetadataPass metadataPass,
            ProPixelizerApplyPixelizationMapToDepth pixelExpandedDepthPass)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            Materials = new MaterialLibrary(resources);
            TargetPass = targetPass;
            OriginalSceneDepth = originalSceneDepth;
            MetadataPass = metadataPass;
            PixelExpandedDepthPass = pixelExpandedDepthPass;
            RecompositionDepthBased = GlobalKeyword.Create(RECOMPOSITION_DEPTH_BASED);
            RecompositionOnlySecondary = GlobalKeyword.Create(RECOMPOSITION_ONLY_SECONDARY);
            DepthOutputOn = GlobalKeyword.Create(DEPTH_OUTPUT_ON);
            PixelArtAAFilterOn = GlobalKeyword.Create(PIXELART_AA_FILTER_ON);
        }
        private const string RECOMPOSITION_DEPTH_BASED = "RECOMPOSITION_DEPTH_BASED";
        private const string RECOMPOSITION_ONLY_SECONDARY = "RECOMPOSITION_ONLY_SECONDARY";
        private const string DEPTH_OUTPUT_ON = "DEPTH_OUTPUT_ON";
        private const string PIXELART_AA_FILTER_ON = "PIXELART_AA_FILTER_ON";
        private readonly GlobalKeyword RecompositionDepthBased;
        private readonly GlobalKeyword RecompositionOnlySecondary;
        private readonly GlobalKeyword DepthOutputOn;
        private readonly GlobalKeyword PixelArtAAFilterOn;
        private MaterialLibrary Materials;

        protected abstract string ProfilingName { get; }

        protected abstract RecompositionMode GetRecompositionMode();

        #region non-RG
        protected ProPixelizerLowResolutionTargetPass TargetPass;
        protected ProPixelizerStoreSceneDepthPass OriginalSceneDepth;
        protected ProPixelizerMetadataPass MetadataPass;
        protected ProPixelizerApplyPixelizationMapToDepth PixelExpandedDepthPass;
        public RTHandle _RecompositionTarget;

        protected abstract RenderTextureDescriptor GetRecompositionTargetDescriptor(in RenderTextureDescriptor cameraTextureDescriptor);
        protected abstract string GetRecompositionTargetName();
        protected abstract bool OutputsDepth();

        #if !PRERG_API_REMOVED

        public RTHandle GetTargetForBlitPassNonRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType) {
            if (sourceType == ProPixelizerBlitPass.ProPixelizerRTHandleType.Depth && OutputsDepth())
                return _RecompositionTarget;
            if (sourceType == ProPixelizerBlitPass.ProPixelizerRTHandleType.Color && !OutputsDepth())
                return _RecompositionTarget;
            throw new ArgumentException("sourceType");
        }

        protected abstract RTHandle GetLowResSourceForNonRGPath(ref RenderingData renderingData);
        protected abstract RTHandle GetSceneSourceForNonRGPath(ref RenderingData renderingData);

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
#pragma warning disable 0618
            var descriptor = GetRecompositionTargetDescriptor(cameraTextureDescriptor);
            RenderingUtils.ReAllocateIfNeeded(ref _RecompositionTarget, descriptor, name: GetRecompositionTargetName());
            ConfigureTarget(_RecompositionTarget);
            ConfigureClear(ClearFlag.None, Color.black);
#pragma warning restore 0618
        }

        public void Dispose()
        {
            _RecompositionTarget?.Release();
        }

        protected RTHandle NonRGGetUnrecomposedSceneDepth(ref RenderingData renderingData)
        {
            //#pragma warning disable 0618
            //            var useColorTargetForDepth = (renderingData.cameraData.renderer.cameraDepthTargetHandle.nameID == BuiltinRenderTextureType.CameraTarget);
            //#pragma warning restore 0618
            //            return useColorTargetForDepth ? ColorTarget(ref renderingData) : DepthTarget(ref renderingData);

            // If we are prepassing, we should take scene depth stored earlier.
            if (Settings.IsStoringOriginalSceneDepthRequired)
                return OriginalSceneDepth._SceneDepth;
            return NonRGGetSceneDepth(ref renderingData);
        }

        protected RTHandle NonRGGetLowResDepth(ref RenderingData renderingData)
        {
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerTargets:
                default:
                    if (Settings.UsePixelExpansion)
                        return PixelExpandedDepthPass.PixelizationMappedDepthInLowResSpace;

                    if (Settings.UsingDepthPrepass && Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera)
                        return MetadataPass.MetadataObjectBuffer_Depth;

                    return TargetPass.Depth;
                case RecompositionLowResSource.SubCamera:
                    return renderingData.cameraData.camera.GetComponent<ProPixelizerCamera>().SubCamera.Depth;
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            var sceneSource = GetSceneSourceForNonRGPath(ref renderingData);
            var sceneDepth = NonRGGetUnrecomposedSceneDepth(ref renderingData);
            var lowResSource = GetLowResSourceForNonRGPath(ref renderingData);
            var lowResDepth = NonRGGetLowResDepth(ref renderingData);

            CommandBuffer buffer = CommandBufferPool.Get(ProfilingName);
            ExecutePass(
                buffer,
                Materials.SceneRecomposition,
                lowResSource,
                lowResDepth,
                sceneSource,
                sceneDepth,
                _RecompositionTarget,
                OutputsDepth(),
                Settings.UsePixelArtUpscalingFilter,
                GetRecompositionMode(),
                RecompositionDepthBased,
                RecompositionOnlySecondary,
                DepthOutputOn,
                PixelArtAAFilterOn
                );

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
        #endif
#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material sceneRecomposition;
            public TextureHandle lowResTarget;
            public TextureHandle lowResDepth;
            public TextureHandle sceneTarget;
            public TextureHandle sceneDepth;
            public TextureHandle recompositedTarget;
            public bool outputsDepth;
            public bool usePixelArtUpscalingFilter;
            public RecompositionMode mode;
            public GlobalKeyword recompositionDepthKeyword;
            public GlobalKeyword recompositionOnlySecondaryKeyword;
            public GlobalKeyword depthOutputKeyword;
            public GlobalKeyword pixelArtAAFilterKeyword;
        }

        protected abstract TextureDesc GetOutputTextureDesc(RenderGraph renderGraph, ContextContainer frameData);

        protected abstract TextureHandle CreateRecomposedTextureHandle(RenderGraph renderGraph, ContextContainer frameData);
        protected abstract TextureHandle GetLowResSource(ContextContainer frameData);
        protected TextureHandle GetLowResDepth(ContextContainer frameData)
        {            
            switch (Settings.RecompositionSource)
            {
                case RecompositionLowResSource.ProPixelizerTargets:
                default:
                    if (Settings.UsePixelExpansion)
                        return frameData.Get<LowResPixelExpanded>().Depth;

                    if (Settings.UsingDepthPrepass && Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera)
                        return frameData.Get<MetadataTargetData>().Depth;

                    return frameData.Get<LowResTargetData>().Depth;
                case RecompositionLowResSource.SubCamera:
                    return frameData.Get<ProPixelizerSubCameraTargets>().Depth;
            }
        }
        protected abstract TextureHandle GetSceneSource(ContextContainer frameData);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lowResTarget = frameData.Get<LowResTargetData>();
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var originalTargets = frameData.Get<ProPixelizerOriginalCameraTargets>();
            var recomposedTarget = CreateRecomposedTextureHandle(renderGraph, frameData);
            var lowResSource = GetLowResSource(frameData);
            var lowResDepth = GetLowResDepth(frameData);
            var sceneDepth = GetUnrecomposedSceneDepth(resources, frameData);
            var sceneSource = GetSceneSource(frameData);

            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilingName, out var passData))
            {
                passData.sceneRecomposition = Materials.SceneRecomposition;
                passData.lowResTarget = lowResSource;
                passData.lowResDepth = lowResDepth;
                passData.sceneTarget = sceneSource;
                passData.sceneDepth = sceneDepth;
                passData.recompositedTarget = recomposedTarget;
                passData.outputsDepth = OutputsDepth();
                passData.usePixelArtUpscalingFilter = Settings.UsePixelArtUpscalingFilter;
                passData.mode = GetRecompositionMode();
                passData.recompositionDepthKeyword = RecompositionDepthBased;
                passData.recompositionOnlySecondaryKeyword = RecompositionOnlySecondary;
                passData.depthOutputKeyword = DepthOutputOn;
                passData.pixelArtAAFilterKeyword = PixelArtAAFilterOn;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.UseTexture(passData.lowResTarget, AccessFlags.Read);
                builder.UseTexture(passData.lowResDepth, AccessFlags.Read);
                builder.UseTexture(passData.sceneTarget, AccessFlags.Read);
                builder.UseTexture(passData.sceneDepth, AccessFlags.Read);
                builder.UseTexture(passData.recompositedTarget, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        data.sceneRecomposition,
                        data.lowResTarget,
                        data.lowResDepth,
                        data.sceneTarget,
                        data.sceneDepth,
                        data.recompositedTarget,
                        data.outputsDepth,
                        data.usePixelArtUpscalingFilter,
                        data.mode,
                        data.recompositionDepthKeyword,
                        data.recompositionOnlySecondaryKeyword,
                        data.depthOutputKeyword,
                        data.pixelArtAAFilterKeyword
                        );
                });
            }
        }

#endif
#endregion

        /// <summary>
        /// Merges low-res and scene targets through depth sorting.
        /// </summary>
        public static void ExecutePass(
            CommandBuffer command,
            Material sceneRecompositionMaterial,
            RTHandle lowResTarget,
            RTHandle lowResDepth,
            RTHandle sceneTarget,
            RTHandle sceneDepth,
            RTHandle recompositedTarget,
            bool depthOutput,
            bool usePixelArtUpscalingFilter,
            RecompositionMode mode,
            GlobalKeyword recompositionDepthKeyword,
            GlobalKeyword recompositionOnlySecondaryKeyword,
            GlobalKeyword depthOutputKeyword,
            GlobalKeyword pixelArtAAFilterKeyword
            )
        {
            //sceneRecompositionMaterial.SetTexture(RecompositionMaterialProperties.InputDepthTexture, sceneDepth, RenderTextureSubElement.Depth);
            sceneRecompositionMaterial.SetTexture(RecompositionMaterialProperties.InputDepthTexture, sceneDepth);
            sceneRecompositionMaterial.SetTexture(RecompositionMaterialProperties.SecondaryTexture, lowResTarget);
            sceneRecompositionMaterial.SetTexture(RecompositionMaterialProperties.SecondaryDepthTexture, lowResDepth);
            //sceneRecompositionMaterial.SetTexture(RecompositionMaterialProperties."_Depth", lowResDepth, RenderTextureSubElement.Depth);

            if (usePixelArtUpscalingFilter)
                command.SetKeyword(pixelArtAAFilterKeyword, true);
            else
                command.SetKeyword(pixelArtAAFilterKeyword, false);
            switch (mode) {
                case RecompositionMode.DepthBasedMerge:
                    command.SetKeyword(recompositionDepthKeyword, true);
                    command.SetKeyword(recompositionOnlySecondaryKeyword, false);
                    break;
                default:
                    command.SetKeyword(recompositionDepthKeyword, false);
                    command.SetKeyword(recompositionOnlySecondaryKeyword, true);
                    break;
            }
            if (sceneTarget == null || sceneTarget.rt == null)
            {
                // This workaround is required for the small thumbnail preview camera in 6000.0.5f1 (maybe earlier too),
                // which is a preview camera that does not use the backbuffer. Normal preview camera uses backbuffer and is fine.
                Blitter.BlitCameraTexture(command, lowResTarget, recompositedTarget);
                return;
            }
            
            if (depthOutput)
                command.EnableKeyword(depthOutputKeyword);
            else
                command.DisableKeyword(depthOutputKeyword);
            Blitter.BlitCameraTexture(command, sceneTarget, recompositedTarget, sceneRecompositionMaterial, 0);            
        }

        /// <summary>
        /// Materials used by this pass.
        /// </summary>
        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;
            public Material SceneRecomposition
            {
                get
                {
                    if (_SceneRecomposition == null)
                        _SceneRecomposition = new Material(Resources.SceneRecomposition);
                    return _SceneRecomposition;
                }
            }
            private Material _SceneRecomposition;

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}
