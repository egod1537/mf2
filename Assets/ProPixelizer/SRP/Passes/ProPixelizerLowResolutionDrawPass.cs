// Copyright Elliot Bentine, 2018-
using Unity.Collections;
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
    /// Renders objects to be pixelated into the low-res render target.
    /// 
    /// This Pass prepares render targets that are used by other passes. This pass retains
    /// responsibility for disposing of these targets.
    /// </summary>
    public class ProPixelizerLowResolutionDrawPass : ProPixelizerPass
    {
        public const string SHADER_PASS_DRAWN = "UNIVERSALFORWARD";

        public ProPixelizerLowResolutionDrawPass(ProPixelizerLowResolutionConfigurationPass configPass, ProPixelizerLowResolutionTargetPass targetPass)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            ConfigPass = configPass;
            TargetPass = targetPass;
        }

        private NativeArray<RenderStateBlock> StateBlocks = default;
        private NativeArray<ShaderTagId> RenderTypes = default;

        public void Dispose()
        {
            if (StateBlocks != default)
            {
                StateBlocks.Dispose();
                StateBlocks = default;
            }
            if (RenderTypes != default)
            {
                RenderTypes.Dispose();
                RenderTypes = default;
            }
        }

        #region non-RG
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(nameof(ProPixelizerLowResolutionDrawPass));
        private ProPixelizerLowResolutionConfigurationPass ConfigPass;
        private ProPixelizerLowResolutionTargetPass TargetPass;
#if !PRERG_API_REMOVED
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer buffer = CommandBufferPool.Get();
            using (new ProfilingScope(buffer, m_ProfilingSampler))
            {
                buffer.SetRenderTarget(
                    ColorTargetForPixelization(ref renderingData, TargetPass),
                    DepthTargetForPixelization(ref renderingData, TargetPass)
                    );
                if (Settings.ClearLowResolutionTargetBeforeDraw)
                    buffer.ClearRenderTarget(true, true, renderingData.cameraData.camera.backgroundColor); // dont use cameraData.backgroundColor -> doesnt exist for 2021 lts
                var drawingSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId(SHADER_PASS_DRAWN), ref renderingData, SortingCriteria.CommonOpaque);
                bool previewCamera = renderingData.cameraData.cameraType == CameraType.Preview;
                var normalView = renderingData.cameraData.GetViewMatrix();
                var normalProjection = renderingData.cameraData.GetProjectionMatrix();

                ProPixelizerUtils.CalculateLowResViewProjection(
                    normalView,
                    normalProjection,
                    renderingData.cameraData.camera.orthographic,
                    renderingData.cameraData.camera.nearClipPlane,
                    renderingData.cameraData.camera.farClipPlane,
                    ConfigPass.Data.OrthoLowResWidth,
                    ConfigPass.Data.OrthoLowResHeight,
                    ConfigPass.Data.LowResCameraDeltaWS,
                    ConfigPass.Data.LowResPerspectiveVerticalFOV,
                    ConfigPass.Data.LowResAspect,
                    Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera,
                    out var viewMatrix,
                    out var projectionMatrix
                );
                var renderTypes = new NativeArray<ShaderTagId>(2, Allocator.Temp);
                var stateBlocks = new NativeArray<RenderStateBlock>(2, Allocator.Temp);
                GetRendererListParams(
                    ref Settings,
                    ref renderingData.cullResults,
                    ref drawingSettings,
                    previewCamera,
                    out var rendererListParams,
                    ref renderTypes,
                    ref stateBlocks
                    );
                var renderList = context.CreateRendererList(ref rendererListParams);

                var usePixelExpansion = Settings.UsePixelExpansion && !previewCamera;
                var restoreNormalMatrices = Settings.Filter == PixelizationFilter.FullScene;

                // Draw the renderer list
                if (Settings.Filter != PixelizationFilter.FullScene)
                {
                    ExecutePass(
#if UNITY_2023_3_OR_NEWER
                        CommandBufferHelpers.GetRasterCommandBuffer(buffer),
#else
                        buffer,
#endif
                        renderList,
                        Settings.Filter,
                        usePixelExpansion,
                        Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera,
                        projectionMatrix,
                        viewMatrix,
                        restoreNormalMatrices ? normalProjection : projectionMatrix,
                        restoreNormalMatrices ? normalView : viewMatrix
                        );
                }
                stateBlocks.Dispose();
                renderTypes.Dispose();

            }
            // restore initial targets.
            SetColorAndDepthTargets(buffer, ref renderingData);
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
#endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER
        class PassData
        {
            public RendererListHandle lowResObjects;
            public bool usePixelExpansion;
            public bool useVirtualCameraScreenSpace;
            public PixelizationFilter pixelizationFilter;
            public Matrix4x4 projection;
            public Matrix4x4 view;
            public Matrix4x4 postProjection;
            public Matrix4x4 postView;
            public NativeArray<ShaderTagId> renderTypes;
            public NativeArray<RenderStateBlock> stateBlocks;
            public bool clearTarget;
            public Color clearColor;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var lowresTarget = frameData.Get<LowResTargetData>();
            var lowResConfiguration = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();
            var lightData = frameData.Get<UniversalLightData>();

            var normalView = cameraData.GetViewMatrix();
            var normalProjection = cameraData.GetProjectionMatrix();

            ProPixelizerUtils.CalculateLowResViewProjection(
                normalView,
                normalProjection,
                cameraData.camera.orthographic,
                cameraData.camera.nearClipPlane, cameraData.camera.farClipPlane,
                lowResConfiguration.OrthoLowResWidth, lowResConfiguration.OrthoLowResHeight,
                lowResConfiguration.LowResCameraDeltaWS,
                lowResConfiguration.LowResPerspectiveVerticalFOV,
                lowResConfiguration.LowResAspect,
                Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera,
                out var viewMatrix,
                out var projectionMatrix
            );

            Dispose();
            RenderTypes = new NativeArray<ShaderTagId>(2, Allocator.Temp);
            StateBlocks = new NativeArray<RenderStateBlock>(2, Allocator.Temp);

            var drawingSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId(SHADER_PASS_DRAWN), renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            GetRendererListParams(
                ref Settings,
                ref renderingData.cullResults,
                ref drawingSettings,
                cameraData.cameraType == CameraType.Preview,
                out var renderListParams,
                ref RenderTypes,
                ref StateBlocks
                );
                

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(ProPixelizerLowResolutionDrawPass), out var passData))
            {
                builder.UseAllGlobalTextures(true);
                passData.lowResObjects = renderGraph.CreateRendererList(renderListParams);
                RenderTypes.Dispose(); RenderTypes = default;
                StateBlocks.Dispose(); StateBlocks = default;
                passData.projection = projectionMatrix;
                passData.view = viewMatrix;
                var restoreNormalMatrices = true;
                passData.postView = restoreNormalMatrices ? normalView : viewMatrix;
                passData.postProjection = restoreNormalMatrices ? normalProjection : projectionMatrix;
                passData.usePixelExpansion = Settings.UsePixelExpansion;
                passData.useVirtualCameraScreenSpace = Settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera;
                passData.pixelizationFilter = Settings.Filter;
                passData.renderTypes = RenderTypes;
                passData.stateBlocks = StateBlocks;
                passData.clearTarget = Settings.ClearLowResolutionTargetBeforeDraw;
                passData.clearColor = cameraData.camera.backgroundColor;

                builder.UseRendererList(passData.lowResObjects);
                builder.SetRenderAttachment(lowresTarget.Color, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(
                    lowresTarget.Depth,
                    passData.clearTarget
                        ? AccessFlags.Write
                        : AccessFlags.ReadWrite
                        );
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                {
                    if (passData.clearTarget)
                        context.cmd.ClearRenderTarget(true, true, passData.clearColor);
                    ExecutePass(context.cmd,
                        passData.lowResObjects,
                        passData.pixelizationFilter,
                        passData.usePixelExpansion,
                        passData.useVirtualCameraScreenSpace,
                        passData.projection,
                        passData.view,
                        passData.postProjection,
                        passData.postView
                        );
                });
            }
        }
#endif
#endregion

        public static void GetRendererListParams(
            ref ProPixelizerSettings settings,
            ref CullingResults cullingResults,
            ref DrawingSettings drawingSettings,
            bool previewCamera,
            out RendererListParams renderListParams,
            ref NativeArray<ShaderTagId> renderTypes,
            ref NativeArray<RenderStateBlock> stateBlocks
            )
        {
            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);
            if (settings.Filter == PixelizationFilter.Layers)
            {
                var layerMask = !previewCamera ? settings.PixelizedLayers.value : -1;
                filteringSettings.layerMask = layerMask;
            }

            //var drawingSettings = RenderingUtils.CreateDrawingSettings(new ShaderTagId("UNIVERSALFORWARD"), ref renderingData, SortingCriteria.CommonOpaque);
            renderListParams = new RendererListParams(cullingResults, drawingSettings, filteringSettings);

            if (settings.Filter == PixelizationFilter.OnlyProPixelizer)
            {
                // Draw only shaders with "RenderType" = "ProPixelizerRenderType"

                stateBlocks[0] = new RenderStateBlock(RenderStateMask.Nothing);
                stateBlocks[1] = new RenderStateBlock(RenderStateMask.Everything)
                {
                    depthState = new DepthState(false, CompareFunction.Never),
                };

                // Create the parameters that tell Unity when to override the render state
                ShaderTagId renderType = new ShaderTagId("ProPixelizer");

                renderTypes[0] = renderType;
                renderTypes[1] = new ShaderTagId(); //default -> 'all others'.

                renderListParams.isPassTagName = false;
                renderListParams.tagName = new ShaderTagId("RenderType");
                renderListParams.stateBlocks = stateBlocks;
                renderListParams.tagValues = renderTypes;
            }
        }

        public static void ExecutePass(
#if UNITY_2023_3_OR_NEWER
            IRasterCommandBuffer command,
#else
            CommandBuffer command,
#endif
            RendererList lowResObjects,
            PixelizationFilter filter,
            bool usePixelExpansion,
            bool useVirtualCameraScreenSpace,
            Matrix4x4 projection,
            Matrix4x4 view,
            Matrix4x4 postProjection, // state to leave in
            Matrix4x4 postView
            )
        {
            if (usePixelExpansion)
                ProPixelizerUtils.SetPixelGlobalScale(command, 1f);
            else
                ProPixelizerUtils.SetPixelGlobalScale(command, 0.01f);
            if (filter == PixelizationFilter.OnlyProPixelizer)
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);

            // Set camera matrices for rendering.
#if UNITY_2023_3_OR_NEWER
            command.SetViewProjectionMatrices(view, projection);
#else
            command.SetViewMatrix(view);
            command.SetProjectionMatrix(projection);
#endif
            command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW, view);
            command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION, projection);
            command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, view.inverse);
            command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, projection.inverse);
            
            command.SetGlobalFloat(
                ProPixelizerFloats.VIRTUAL_CAMERA_LOW_RES_DRAW,
                useVirtualCameraScreenSpace ? 1.0f : 0.0f
                );
            command.DrawRendererList(lowResObjects);
            command.SetGlobalFloat(ProPixelizerFloats.VIRTUAL_CAMERA_LOW_RES_DRAW, 0.0f);

#if UNITY_2023_3_OR_NEWER
            command.SetViewProjectionMatrices(postView, postProjection);
#else
            command.SetViewMatrix(postView);
            command.SetProjectionMatrix(postProjection);
#endif
        }
    }
}
