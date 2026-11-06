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
    /// Creates the pixelization map used by ProPixelizer
    /// </summary>
    public class ProPixelizerPixelizationMapPass : ProPixelizerPass
    {
        public ProPixelizerPixelizationMapPass(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configPass,
            ProPixelizerLowResolutionTargetPass targetPass
            )
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
            Materials = new MaterialLibrary(shaders);
            MetadataPass = metadata;
            ConfigPass = configPass;
            TargetPass = targetPass;

            ORTHO_PROJECTION = new LocalKeyword(shaders.PixelizationMap, "ORTHO_PROJECTION");
            OVERLAY_CAMERA = new LocalKeyword(shaders.PixelizationMap, "OVERLAY_CAMERA");
        }

        private MaterialLibrary Materials;

        #region non-RG
        private ProPixelizerMetadataPass MetadataPass;
        private ProPixelizerLowResolutionConfigurationPass ConfigPass;
        private ProPixelizerLowResolutionTargetPass TargetPass;
        public RTHandle PixelizationMap;
#if !PRERG_API_REMOVED
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
#if DISABLE_PROPIX_PREVIEW_WINDOW
            // Inspector window in 2022.2 is bugged with a null render target.
            // https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
            // https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            var pixelizationMapDescriptor = cameraTextureDescriptor;
            pixelizationMapDescriptor.useMipMap = false;
            if (ConfigPass.Data.Width > 0)
            {
                pixelizationMapDescriptor.width = ConfigPass.Data.Width;
                pixelizationMapDescriptor.height = ConfigPass.Data.Height;
            }
            pixelizationMapDescriptor.colorFormat = RenderTextureFormat.ARGB32;
            pixelizationMapDescriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            pixelizationMapDescriptor.depthBufferBits = 0;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref PixelizationMap, pixelizationMapDescriptor, name: ProPixelizerTargets.PROPIXELIZER_PIXELIZATION_MAP);
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
#if DISABLE_PROPIX_PREVIEW_WINDOW
            // Inspector window in 2022.2 is bugged with a null render target.
            // https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
            // https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;
#endif

            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerPixelizationMapPass));
            buffer.name = nameof(ProPixelizerPixelizationMapPass);

            ExecutePass(
                buffer,
                ref Settings,
                Materials.PixelizationMap,
                renderingData.cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay,
                renderingData.cameraData.camera.orthographic,
                ORTHO_PROJECTION,
                OVERLAY_CAMERA,
                MetadataPass.MetadataObjectBuffer,
                MetadataPass.MetadataObjectBuffer_Depth,
                DepthTargetForPixelization(ref renderingData, TargetPass),
                PixelizationMap
                );

            // ...and restore transformations:
            ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);

        }


        public void Dispose()
        {
            PixelizationMap?.Release();
        }
#endif
#endregion non-RG

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public ProPixelizerSettings settings;
            public Material material;
            public bool overlay;
            public bool orthographic;
            public LocalKeyword ORTHO_PROJECTION;
            public LocalKeyword OVERLAY_CAMERA;
            public TextureHandle metadataColor;
            public TextureHandle metadataDepth;
            public TextureHandle sceneDepth;
            public TextureHandle pixelizationMap;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var metadataTarget = frameData.Get<MetadataTargetData>();
            var lowResConfiguration = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var cameraData = frameData.Get<UniversalCameraData>();
            var mapData = frameData.GetOrCreate<PixelizationMapData>();
            var resources = frameData.Get<UniversalResourceData>();

            var mapDescriptor = metadataTarget.Color.GetDescriptor(renderGraph);
            mapDescriptor.useMipMap = false;
            mapDescriptor.colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm;
            mapDescriptor.depthBufferBits = DepthBits.None;
            mapDescriptor.name = ProPixelizerTargets.PROPIXELIZER_PIXELIZATION_MAP;
            mapDescriptor.clearBuffer = true;
            mapDescriptor.filterMode = FilterMode.Point;
            mapData.Map = renderGraph.CreateTexture(mapDescriptor);

            using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerPixelizationMapPass), out var passData))
            {
                passData.settings = Settings;
                passData.material = Materials.PixelizationMap;
                passData.overlay = cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay;
                passData.orthographic = cameraData.camera.orthographic;
                passData.ORTHO_PROJECTION = ORTHO_PROJECTION;
                passData.OVERLAY_CAMERA = OVERLAY_CAMERA;
                passData.metadataColor = metadataTarget.Color;
                passData.metadataDepth = metadataTarget.Depth;
                passData.sceneDepth = GetSceneDepthTexture(resources);
                passData.pixelizationMap = mapData.Map;

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.UseTexture(metadataTarget.Color, AccessFlags.Read);
                builder.UseTexture(metadataTarget.Depth, AccessFlags.Read);
                builder.UseTexture(passData.sceneDepth, AccessFlags.Read);
                builder.UseTexture(mapData.Map, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        ref data.settings,
                        data.material,
                        data.overlay,
                        data.orthographic,
                        data.ORTHO_PROJECTION,
                        data.OVERLAY_CAMERA,
                        data.metadataColor,
                        data.metadataDepth,
                        data.sceneDepth,
                        data.pixelizationMap
                        );
                });
            }
        }

#endif
#endregion

        readonly LocalKeyword ORTHO_PROJECTION;
        readonly LocalKeyword OVERLAY_CAMERA;

        public static void ExecutePass(
            CommandBuffer command,
            ref ProPixelizerSettings settings,
            Material material,
            bool overlay,
            bool orthographic,
            LocalKeyword ORTHO_PROJECTION,
            LocalKeyword OVERLAY_CAMERA,
            RTHandle metadataColor,
            RTHandle metadataDepth,
            RTHandle sceneDepth,
            RTHandle pixelizationMap
            )
        {
            if (!settings.UsePixelExpansion)
                return;
            // Configure keywords for pixelising material.
            if (orthographic)
                command.EnableKeyword(material, ORTHO_PROJECTION);
            else
                command.DisableKeyword(material, ORTHO_PROJECTION);

            if (overlay)
                command.EnableKeyword(material, OVERLAY_CAMERA);
            else
                command.DisableKeyword(material, OVERLAY_CAMERA);

            material.SetTexture(PixelizationMapMaterialProperties.MainTex, metadataColor);
            material.SetTexture(PixelizationMapMaterialProperties.SourceDepthTexture, metadataDepth, RenderTextureSubElement.Depth);
            material.SetTexture(PixelizationMapMaterialProperties.SceneDepthTexture, sceneDepth);
            Blitter.BlitCameraTexture(command, metadataColor, pixelizationMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, material, 0);
        }

        /// <summary>
        /// Materials used by the PixelizationPass
        /// </summary>
        public sealed class MaterialLibrary
        {
            private ShaderResources Resources;

            private Material _PixelizationMap;
            public Material PixelizationMap
            {
                get
                {
                    if (_PixelizationMap == null)
                        _PixelizationMap = new Material(Resources.PixelizationMap);
                    return _PixelizationMap;
                }
            }

            public MaterialLibrary(ShaderResources resources)
            {
                Resources = resources;
            }
        }
    }
}