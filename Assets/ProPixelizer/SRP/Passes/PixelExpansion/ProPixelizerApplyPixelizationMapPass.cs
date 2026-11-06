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
    public abstract class ProPixelizerApplyPixelizationMapPass : ProPixelizerPass
    {
        public ProPixelizerApplyPixelizationMapPass(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configPass,
            ProPixelizerLowResolutionTargetPass targetPass,
            ProPixelizerPixelizationMapPass mapPass
            )
        {
            Materials = new MaterialLibrary(shaders);
            ConfigPass = configPass;
            TargetPass = targetPass;
            MetadataPass = metadata;
            MapPass = mapPass;
        }

        private static readonly string DEPTH_OUTPUT_KEYWORD = "PIXELMAP_DEPTH_OUTPUT_ON";
        protected readonly GlobalKeyword ApplyPixelMapDepthOutputKeyword = GlobalKeyword.Create(DEPTH_OUTPUT_KEYWORD);

        private MaterialLibrary Materials;

        protected abstract string ProfilingName { get; }
        protected abstract bool OutputsDepth();
        protected bool UseUnpixelatedSceneDepth() => 
            Settings.UsingRedirection
            || !(Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera && Settings.Filter == PixelizationFilter.OnlyProPixelizer);

        #region non-RG
        protected ProPixelizerLowResolutionConfigurationPass ConfigPass;
        protected ProPixelizerLowResolutionTargetPass TargetPass;
        protected ProPixelizerPixelizationMapPass MapPass;
        protected ProPixelizerMetadataPass MetadataPass;

#if !PRERG_API_REMOVED
        protected abstract RenderTextureDescriptor GetOutputTextureDescriptor(RenderTextureDescriptor cameraTextureDescriptor);
        protected abstract RTHandle NonRGGetSourceRT(ref RenderingData renderingData);
        protected abstract RTHandle NonRGGetSceneDepthRT(ref RenderingData renderingData);

        private RTHandle _PointSampledSource;
        protected RTHandle _Expanded;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var intermediateDescriptor = GetOutputTextureDescriptor(cameraTextureDescriptor);
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _PointSampledSource, intermediateDescriptor, name: "ProP_ApplyPixelMap_PointSampledSource", wrapMode: TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref _Expanded, intermediateDescriptor, name: "ProP_ApplyPixelMap_Expanded", wrapMode: TextureWrapMode.Clamp);
            ConfigureTarget(_Expanded);
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer command = CommandBufferPool.Get(ProfilingName);
            var usePixelExpansion = Settings.UsePixelExpansion && renderingData.cameraData.cameraType != CameraType.Preview;
            var resampleSource = !OutputsDepth();

            // ExecutePass
            ExecutePass(
                command,
                Materials.ApplyPixelizationMap,
                usePixelExpansion,
                MetadataPass.MetadataObjectBuffer_Depth,
                NonRGGetSceneDepthRT(ref renderingData),
                MapPass.PixelizationMap,
                _PointSampledSource,
                _Expanded,
                NonRGGetSourceRT(ref renderingData),
                UseUnpixelatedSceneDepth(),
                OutputsDepth(),
                ApplyPixelMapDepthOutputKeyword
                );

            // ...and restore transformations:
            ProPixelizerUtils.SetCameraMatrices(command, ref renderingData.cameraData);

            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
        }

        public void Dispose()
        {
            _Expanded?.Release();
            _PointSampledSource?.Release();
        }
#endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material applyPixelizationMap;
            public bool usePixelExpansion;
            public TextureHandle MetadataDepth;
            public TextureHandle PixelizationMap;
            public TextureHandle SceneDepth;
            public TextureHandle PointSampledCopy;
            public TextureHandle Intermediate;
            public TextureHandle Target;
            public bool UseUnpixelatedSceneDepth;
            public bool OutputsDepth;
            public GlobalKeyword depthOutputKeyword;
        }

        //protected abstract TextureHandle GetSourceRG(RenderGraph renderGraph, ContextContainer frameData);
        protected abstract TextureHandle GetSourceRG(ContextContainer frameData);
        protected abstract TextureHandle GetExpandedRG(RenderGraph renderGraph, ContextContainer frameData);
        protected abstract TextureHandle GetPointFilteredSourceRG(RenderGraph renderGraph, ContextContainer frameData);
        protected abstract TextureHandle GetUnpixelizedSceneDepthRG(ContextContainer frameData);

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var metadataTarget = frameData.Get<MetadataTargetData>();
            var mapData = frameData.Get<PixelizationMapData>();

            var targetRG = GetSourceRG(frameData);
            var sceneDepthTexture = GetUnpixelizedSceneDepthRG(frameData);
            var outputsDepth = OutputsDepth();
            var resampleSource = !outputsDepth;
            var pointSampledCopy = resampleSource ? GetPointFilteredSourceRG(renderGraph, frameData) : TextureHandle.nullHandle;
            var intermediate = GetExpandedRG(renderGraph, frameData);
            var applyPixelMapDepthOutputKeyword = ApplyPixelMapDepthOutputKeyword;

            //var targetIsSceneDepth = targetRG.GetDescriptor(renderGraph).name == sceneDepthTexture.GetDescriptor(renderGraph).name;
            var targetIsSceneDepth = targetRG == sceneDepthTexture;

            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilingName, out var passData))
            {
                passData.applyPixelizationMap = Materials.ApplyPixelizationMap;
                passData.usePixelExpansion = Settings.UsePixelExpansion;
                passData.MetadataDepth = metadataTarget.Depth;
                passData.PixelizationMap = mapData.Map;
                passData.SceneDepth = sceneDepthTexture;
                passData.PointSampledCopy = pointSampledCopy;
                passData.Intermediate = intermediate;
                passData.Target = targetRG;
                passData.OutputsDepth = outputsDepth;
                passData.UseUnpixelatedSceneDepth = UseUnpixelatedSceneDepth();
                passData.depthOutputKeyword = applyPixelMapDepthOutputKeyword;

                builder.UseTexture(passData.MetadataDepth, AccessFlags.Read);
                builder.UseTexture(passData.PixelizationMap, AccessFlags.Read);
                if (!targetIsSceneDepth)
                    builder.UseTexture(passData.SceneDepth, AccessFlags.Read);
                if (resampleSource)
                    builder.UseTexture(passData.PointSampledCopy, AccessFlags.ReadWrite);
                builder.UseTexture(passData.Intermediate, AccessFlags.Write);
                builder.UseTexture(passData.Target, AccessFlags.Read);

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        data.applyPixelizationMap,
                        data.usePixelExpansion,
                        data.MetadataDepth,
                        data.SceneDepth,
                        data.PixelizationMap,
                        data.PointSampledCopy,
                        data.Intermediate,
                        data.Target,
                        data.UseUnpixelatedSceneDepth,
                        data.OutputsDepth,
                        data.depthOutputKeyword
                        );
                });
            }
        }

#endif
        #endregion

        public static void ExecutePass(
            CommandBuffer buffer,
            Material applyPixelizationMap,
            bool usePixelExpansion,
            RTHandle MetadataDepth,
            RTHandle SceneDepth,
            RTHandle PixelizationMap,
            RTHandle PointSampledSource,
            RTHandle Expanded, // point-filtered intermediate target
            RTHandle Target,
            bool useSceneDepth,
            bool outputsDepth,
            GlobalKeyword depthOutputKeyword
            )
        {
            if (!usePixelExpansion)
                return;

            var pointSampledSource = Target;
            bool usePointFilterToResample = !outputsDepth;
            if (usePointFilterToResample)
            {
                Blitter.BlitCameraTexture(buffer, Target, PointSampledSource);
                pointSampledSource = PointSampledSource;
            }

            var farDepthTexture = SystemInfo.usesReversedZBuffer ? Texture2D.blackTexture : Texture2D.whiteTexture;

            // Pixelise the appearance texture
            applyPixelizationMap.SetTexture(ApplyPixelizationMapMaterialProperties.MainTex, pointSampledSource);
            applyPixelizationMap.SetTexture(ApplyPixelizationMapMaterialProperties.PixelizationMap, PixelizationMap);
            applyPixelizationMap.SetTexture(ApplyPixelizationMapMaterialProperties.SourceDepthTexture, MetadataDepth, RenderTextureSubElement.Depth);
            applyPixelizationMap.SetTexture(ApplyPixelizationMapMaterialProperties.SceneDepthTexture, useSceneDepth ? SceneDepth : farDepthTexture);
            if (outputsDepth)
                buffer.EnableKeyword(depthOutputKeyword);
            else
                buffer.DisableKeyword(depthOutputKeyword);
            Blitter.BlitCameraTexture(buffer, pointSampledSource, Expanded, applyPixelizationMap, 0);
        }

        /// <summary>
        /// Materials used by the PixelizationPass
        /// </summary>
        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;

            private Material _ApplyPixelizationMap;
            public Material ApplyPixelizationMap
            {
                get
                {
                    if (_ApplyPixelizationMap == null)
                        _ApplyPixelizationMap = new Material(Resources.ApplyPixelizationMap);
                    return _ApplyPixelizationMap;
                }
            }

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}
