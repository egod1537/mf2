// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Windows;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Depth-based recomposition may occur for the color buffer after depth buffer (for example, when using hybrid/Only ProPixelizer filter and depth prepass).
    /// When this occurs, we need to store the original scene depth _before_ applying recomposition so that we know how to merge the color buffer.
    /// </summary>
    public class ProPixelizerStoreSceneDepthPass : ProPixelizerPass
    {
        public ProPixelizerStoreSceneDepthPass(ShaderResources resources, ProPixelizerLowResolutionTargetPass targetPass)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            Materials = new MaterialLibrary(resources);
            TargetPass = targetPass;
        }
        private MaterialLibrary Materials;        

        #region non-RG
        public RTHandle _SceneDepth;
        public ProPixelizerLowResolutionTargetPass TargetPass;
        #if !PRERG_API_REMOVED

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var depthDescriptor = cameraTextureDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _SceneDepth, depthDescriptor, name: ProPixelizerTargets.PROPIXELIZER_ORIGINAL_SCENE_DEPTH);
            ConfigureTarget(_SceneDepth);
            ConfigureClear(ClearFlag.None, Color.black);
#pragma warning restore 0618
        }

        public void Dispose()
        {
            _SceneDepth?.Release();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerStoreSceneDepthPass));
            var source = Settings.UsingRedirection && Settings.PrepassConfiguration == PipelinePrepassConfiguration.None ? TargetPass.Depth : NonRGGetSceneDepth(ref renderingData);
            if (source != null && source.rt != null)
            {
                Blitter.BlitCameraTexture(buffer, source, _SceneDepth, Materials.CopyDepth, 0);
            }
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
        #endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public TextureHandle Source;
            public TextureHandle Target;
            public Material copyDepth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var camera = frameData.Get<UniversalCameraData>();
            var original = frameData.GetOrCreate<SceneDepthBeforeRecomposition>();
            
            var originalDepthTexture = GetSceneDepthTexture(resources);
            var depthDescriptor = originalDepthTexture.GetDescriptor(renderGraph);
            depthDescriptor.name = ProPixelizerTargets.PROPIXELIZER_ORIGINAL_SCENE_DEPTH;
            depthDescriptor.clearBuffer = false;
            original.Depth = renderGraph.CreateTexture(depthDescriptor);

            using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerStoreSceneDepthPass), out var passData))
            {
                passData.Source = originalDepthTexture;
                passData.Target = original.Depth;
                passData.copyDepth = Materials.CopyDepth;

                builder.AllowPassCulling(false);
                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Target, AccessFlags.Write);
                builder.SetRenderFunc(
                    (PassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePass(
                            CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                            data
                            );
                    });
            }
        }

        static void ExecutePass(CommandBuffer command, PassData data)
        {
            Blitter.BlitCameraTexture(command, data.Source, data.Target, data.copyDepth, 0);
        }

#endif
#endregion

        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;
            public Material CopyDepth
            {
                get
                {
                    if (_CopyDepth == null)
                        _CopyDepth = new Material(Resources.CopyDepth);
                    return _CopyDepth;
                }
            }
            private Material _CopyDepth;

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}