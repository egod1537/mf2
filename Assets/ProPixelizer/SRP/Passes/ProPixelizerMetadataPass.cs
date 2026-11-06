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
    /// <summary>
    /// Renders object shaders with LightMode=ProPixelizer to ProPixelizer_Metadata.
	/// 
	/// Requires the size of the buffers to be predetermined.
    /// </summary>
    public class ProPixelizerMetadataPass : ProPixelizerPass
	{

        public ProPixelizerMetadataPass(ProPixelizerLowResolutionConfigurationPass config)
		{
			renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            ConfigurationPass = config;
		}

        #region Non-RG
        /// <summary>
        /// Buffer into which objects are rendered using the Outline pass.
        /// </summary>
        public RTHandle MetadataObjectBuffer;
        public RTHandle MetadataObjectBuffer_Depth;

        ProfilingSampler m_ProfilingSampler = new ProfilingSampler(nameof(ProPixelizerMetadataPass));
        private ProPixelizerLowResolutionConfigurationPass ConfigurationPass;
#if !PRERG_API_REMOVED
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			var metadataDescriptor = cameraTextureDescriptor;
			metadataDescriptor.useMipMap = false;
			metadataDescriptor.colorFormat = RenderTextureFormat.ARGB32;
			metadataDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            if (ConfigurationPass.Data.Width > 0)
            {
                metadataDescriptor.width = ConfigurationPass.Data.Width;
                metadataDescriptor.height = ConfigurationPass.Data.Height;
            }
            var depthDescriptor = metadataDescriptor;
			depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;
			metadataDescriptor.depthBufferBits = 0;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref MetadataObjectBuffer, metadataDescriptor, name: ProPixelizerTargets.PROPIXELIZER_METADATA_BUFFER, wrapMode: TextureWrapMode.Clamp);
            RenderingUtils.ReAllocateIfNeeded(ref MetadataObjectBuffer_Depth, depthDescriptor, name: ProPixelizerTargets.PROPIXELIZER_METADATA_BUFFER_DEPTH, wrapMode: TextureWrapMode.Clamp);
			ConfigureTarget(MetadataObjectBuffer, MetadataObjectBuffer_Depth);
			ConfigureClear(ClearFlag.All, new Color(0.5f, 1f, 0.5f, 0f));
#pragma warning restore 0618
        }

        public void Dispose()
        {
            MetadataObjectBuffer?.Release();
            MetadataObjectBuffer_Depth?.Release();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer buffer = CommandBufferPool.Get();
            using (new ProfilingScope(buffer, m_ProfilingSampler))
            {
                var sort = new SortingSettings(renderingData.cameraData.camera);
                var drawingSettings = new DrawingSettings(ProPixelizerMetadataLightmodeShaderTag, sort);
                var filteringSettings = new FilteringSettings(RenderQueueRange.all);
                if (Settings.Filter == PixelizationFilter.Layers)
                {
                    // In layer mode, metadata is only rendered for layered objects.
                    // In preview mode, treat all as pixelized.
                    var layerMask = renderingData.cameraData.cameraType != CameraType.Preview ? Settings.PixelizedLayers.value : int.MaxValue;
                    filteringSettings.layerMask = layerMask;
                }

                var renderListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                var renderList = context.CreateRendererList(ref renderListParams);
                buffer.SetRenderTarget(MetadataObjectBuffer, MetadataObjectBuffer_Depth); //in compatibility path before

                var normalView = renderingData.cameraData.GetViewMatrix();
                var normalProjection = renderingData.cameraData.GetProjectionMatrix();

                ProPixelizerUtils.CalculateLowResViewProjection(
                    normalView,
                    normalProjection,
                    renderingData.cameraData.camera.orthographic,
                    renderingData.cameraData.camera.nearClipPlane,
                    renderingData.cameraData.camera.farClipPlane,
                    ConfigurationPass.Data.OrthoLowResWidth, 
                    ConfigurationPass.Data.OrthoLowResHeight,
                    ConfigurationPass.Data.LowResCameraDeltaWS,
                    ConfigurationPass.Data.LowResPerspectiveVerticalFOV,
                    ConfigurationPass.Data.LowResAspect,
                    Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera,
                    out var viewMatrix, out var projectionMatrix);

                var usePixelExpansion = Settings.UsePixelExpansion && renderingData.cameraData.cameraType != CameraType.Preview;

                ExecutePass(
#if UNITY_2023_3_OR_NEWER
                    CommandBufferHelpers.GetRasterCommandBuffer(buffer),
#else
                    buffer,
#endif
                    renderList,
                    Settings.Filter,
                    usePixelExpansion,
                    projectionMatrix,
                    viewMatrix,
                    normalProjection,
                    normalView
                    );

                // Restore camera matrices
                ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);
            }

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
#endif
#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER
        public class PassData
        {
            public RendererListHandle metadataObjectsToDraw;
            public PixelizationFilter filter;
            public bool usePixelExpansion;
            public Matrix4x4 projection;
            public Matrix4x4 view;
            public Matrix4x4 postProjection; // left in this state after pass
            public Matrix4x4 postView; // left in this state after pass
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var metadataTarget = frameData.GetOrCreate<MetadataTargetData>();
            var lowResConfiguration = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.GetOrCreate<UniversalRenderingData>();
            var originalTargets = frameData.Get<ProPixelizerOriginalCameraTargets>();

            var colorDescriptor = originalTargets.Color.GetDescriptor(renderGraph);
            var cameraDepthDescriptor = originalTargets.Depth.GetDescriptor(renderGraph);
            colorDescriptor.useMipMap = false;
            colorDescriptor.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            colorDescriptor.width = lowResConfiguration.Width;
            colorDescriptor.height = lowResConfiguration.Height;
            colorDescriptor.name = ProPixelizerTargets.PROPIXELIZER_METADATA_BUFFER;
            colorDescriptor.clearBuffer = false;
            colorDescriptor.filterMode = FilterMode.Point;
            colorDescriptor.wrapMode = TextureWrapMode.Clamp;
            var depthDescriptor = colorDescriptor;
            depthDescriptor.format = RenderGraphResources.Utils.GetDepthFormat(cameraDepthDescriptor);
            depthDescriptor.name = ProPixelizerTargets.PROPIXELIZER_METADATA_BUFFER_DEPTH;
            depthDescriptor.clearBuffer = false;
            depthDescriptor.wrapMode = TextureWrapMode.Clamp;
            metadataTarget.Color = renderGraph.CreateTexture(colorDescriptor);
            metadataTarget.Depth = renderGraph.CreateTexture(depthDescriptor);

            var sort = new SortingSettings(cameraData.camera);
            var drawingSettings = new DrawingSettings(ProPixelizerMetadataLightmodeShaderTag, sort);
            var filteringSettings = new FilteringSettings(RenderQueueRange.all);

            if (Settings.Filter == PixelizationFilter.Layers)
            {
                // In layer mode, metadata is only rendered for layered objects.
                // In preview mode, treat all as pixelized.
                var layerMask = cameraData.cameraType != CameraType.Preview ? Settings.PixelizedLayers.value : int.MaxValue;
                filteringSettings.layerMask = layerMask;
            }

            var renderListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

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

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(nameof(ProPixelizerMetadataPass), out var passData))
            {
                passData.metadataObjectsToDraw = renderGraph.CreateRendererList(renderListParams);
                passData.usePixelExpansion = Settings.UsePixelExpansion;
                passData.filter = Settings.Filter;
                passData.projection = projectionMatrix;
                passData.view = viewMatrix;
                passData.postProjection = normalProjection;
                passData.postView = normalView;

                builder.UseRendererList(passData.metadataObjectsToDraw);
                builder.SetRenderAttachment(metadataTarget.Color, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(metadataTarget.Depth, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData passData, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(true, true, new Color(0.5f, 1f, 0.5f, 0f));
                    ExecutePass(context.cmd, 
                        passData.metadataObjectsToDraw,
                        passData.filter,
                        passData.usePixelExpansion,
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

#if UNITY_2023_3_OR_NEWER
        public static void ExecutePass(IRasterCommandBuffer command,
#else
        public static void ExecutePass(CommandBuffer command,
#endif
            RendererList metadataObjectsToDraw,
            PixelizationFilter filter,
            bool usePixelExpansion,
            Matrix4x4 projection,
            Matrix4x4 view,
            Matrix4x4 postProjection, // left in these states.
            Matrix4x4 postView
            )
        {
            if (usePixelExpansion)
                ProPixelizerUtils.SetPixelGlobalScale(command, 1f);
            else
                ProPixelizerUtils.SetPixelGlobalScale(command, 0.01f);
            //if (filter == PixelizationFilter.OnlyProPixelizer)
            command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);

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
            command.DrawRendererList(metadataObjectsToDraw);

#if UNITY_2023_3_OR_NEWER
            command.SetViewProjectionMatrices(postView, postProjection);
#else
            command.SetViewMatrix(postView);
            command.SetProjectionMatrix(postProjection);
#endif

            // Disable ProPixelizer pass variable
            if (filter == PixelizationFilter.OnlyProPixelizer)
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 0.0f);
        }
    }
}
