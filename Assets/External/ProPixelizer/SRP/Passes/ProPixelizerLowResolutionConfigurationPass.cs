// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Configures the ProPixelizer low resolution target.
    /// </summary>
    public class ProPixelizerLowResolutionConfigurationPass : ProPixelizerPass
    {
        public ProPixelizerLowResolutionConfigurationPass() : base()
        {
            renderPassEvent = RenderPassEvent.BeforeRendering;
#if UNITY_6000_0_OR_NEWER
            requiresIntermediateTexture = true;
#endif
            Data = new ProPixelizerTargetConfiguration();
        }

        public ProPixelizerTargetConfiguration Data;

        #region non-RG

#if !PRERG_API_REMOVED
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            // Store the render target's size in a global variable.
            // This is required because Unity's RenderScale is not reliable, see
            // e.g. https://forum.unity.com/threads/_scaledscreenparameters-and-render-target-subregion.1336277/
            var handleProps = RTHandles.rtHandleProperties;
            cmd.SetGlobalVector(ProPixelizerVec4s.RENDER_TARGET_INFO, new Vector4(Data.Width, Data.Height, 1.0f, 1.0f));
            cmd.SetGlobalVector(ProPixelizerVec4s.SCREEN_TARGET_INFO, new Vector4(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 1.0f, 1.0f));
            cmd.SetGlobalVector(ProPixelizerVec4s.ORTHO_SIZES, new Vector4(Data.OrthoLowResWidth, Data.OrthoLowResHeight, Data.CameraOrthoWidth, Data.CameraOrthoHeight));
            cmd.SetGlobalVector(ProPixelizerVec4s.LOW_RES_CAMERA_DELTA_UV, new Vector4(Data.LowResCameraDeltaUV.x, Data.LowResCameraDeltaUV.y, 0f, 0f));
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            Data = ProPixelizerTargetConfiguration.From(
                new CameraTargetDescriptor
                {
                    width = renderingData.cameraData.cameraTargetDescriptor.width,
                    height = renderingData.cameraData.cameraTargetDescriptor.height
                },
                renderingData.cameraData.camera,
                ref Settings
            );
        }

#endif

#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER
        class PassData
        {
            public Vector4 RenderTargetInfo;
            public Vector4 ScreenTargetInfo;
            public Vector4 OrthoSizes;
            public Vector4 LowResCameraDeltaUV;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            var configCI = frameData.GetOrCreate<ProPixelizerTargetConfigurationContextItem>();
            var cameraTargetDesc = resources.cameraColor.GetDescriptor(renderGraph);
            var config = ProPixelizerTargetConfiguration.From(
                new CameraTargetDescriptor { width = cameraTargetDesc.width, height = cameraTargetDesc.height },
                cameraData.camera,
                ref Settings
                );
            configCI.Config = config;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(ProPixelizerLowResolutionConfigurationPass), out var passData))
            {
                var handleProps = RTHandles.rtHandleProperties;
                passData.RenderTargetInfo = new Vector4(config.Width, config.Height, handleProps.rtHandleScale.x, handleProps.rtHandleScale.y);
                passData.ScreenTargetInfo = new Vector4(cameraTargetDesc.width, cameraTargetDesc.height, handleProps.rtHandleScale.x, handleProps.rtHandleScale.y);
                passData.OrthoSizes = new Vector4(config.OrthoLowResWidth, config.OrthoLowResHeight, config.CameraOrthoWidth, config.CameraOrthoHeight);
                passData.LowResCameraDeltaUV = new Vector4(config.LowResCameraDeltaUV.x, config.LowResCameraDeltaUV.y, 0f, 0f);

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalVector(ProPixelizerVec4s.RENDER_TARGET_INFO, data.RenderTargetInfo);
                    context.cmd.SetGlobalVector(ProPixelizerVec4s.SCREEN_TARGET_INFO, data.ScreenTargetInfo);
                    context.cmd.SetGlobalVector(ProPixelizerVec4s.ORTHO_SIZES, data.OrthoSizes);
                    context.cmd.SetGlobalVector(ProPixelizerVec4s.LOW_RES_CAMERA_DELTA_UV, data.LowResCameraDeltaUV);
                });
            }
        }
#endif
#endregion
    }
}