// Copyright Elliot Bentine, 2018-
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using ProPixelizer.RenderGraphResources;
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
#pragma warning disable 0672

namespace ProPixelizer
{
#if UNITY_2023_3_OR_NEWER
    /// <summary>
    /// Redirects draw calls 
    /// </summary>
    public class ProPixelizerRenderGraphRedirectPass : ProPixelizerPass
    {
        public ProPixelizerRenderGraphRedirectPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
        }

        public UniversalRenderer _Renderer;
        readonly static FieldInfo OpaqueForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderOpaqueForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);
        readonly static FieldInfo TransparentForwardRendererFieldGetter = typeof(UniversalRenderer).GetField("m_RenderTransparentForwardPass", BindingFlags.NonPublic | BindingFlags.Instance);

        class PassData
        {
            public bool preview;
            public ProPixelizerSettings settings;
            public TextureHandle LowResColor;
            public TextureHandle LowResDepth;
            public Color clearColor;
            public bool baseCamera;
            public Matrix4x4 projection;
            public Matrix4x4 view;
            public int LowresTargetWidth;
            public int LowresTargetHeight;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lowRes = frameData.Get<LowResTargetData>();
            var lowResConfig = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;

            if (Settings.Filter == PixelizationFilter.FullScene) {
                resources.cameraColor = lowRes.Color;
                resources.cameraDepth = lowRes.Depth;
            }

            // change filter settings for opaques and transparents
            var pixelizedLayerMask = cameraData.cameraType != CameraType.Preview ? Settings.PixelizedLayers.value : int.MaxValue;
            var cullingMask = Settings.Filter != PixelizationFilter.Layers ? cameraData.camera.cullingMask : cameraData.camera.cullingMask & ~pixelizedLayerMask;

            var opaqueDrawPass = (DrawObjectsPass)OpaqueForwardRendererFieldGetter.GetValue(_Renderer);
            var transparentDrawPass = (DrawObjectsPass)TransparentForwardRendererFieldGetter.GetValue(_Renderer);

            var opaqueFilteringSettings = ProPixelizerUtils.RendererDrawObjectFilterSettingsUtil.GetDrawPassFilterSettings(opaqueDrawPass);
            opaqueFilteringSettings.layerMask = cullingMask;
            ProPixelizerUtils.RendererDrawObjectFilterSettingsUtil.SetDrawPassFilterSettings(opaqueDrawPass, opaqueFilteringSettings);

            var transparentFilteringSettings = ProPixelizerUtils.RendererDrawObjectFilterSettingsUtil.GetDrawPassFilterSettings(transparentDrawPass);
            transparentFilteringSettings.layerMask = cullingMask;
            ProPixelizerUtils.RendererDrawObjectFilterSettingsUtil.SetDrawPassFilterSettings(transparentDrawPass, transparentFilteringSettings);

            ProPixelizerUtils.CalculateLowResViewProjection(
                cameraData.GetViewMatrix(),
                cameraData.GetProjectionMatrix(),
                cameraData.camera.orthographic,
                cameraData.camera.nearClipPlane,
                cameraData.camera.farClipPlane,
                lowResConfig.OrthoLowResWidth,
                lowResConfig.OrthoLowResHeight,
                lowResConfig.LowResCameraDeltaWS,
                lowResConfig.LowResPerspectiveVerticalFOV,
                lowResConfig.LowResAspect,
                Settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera,
                out var viewMatrix,
                out var projectionMatrix
                );

            using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerRenderGraphRedirectPass), out var passData))
            {
                passData.settings = Settings;
                passData.LowResColor = lowRes.Color;
                passData.LowResDepth = lowRes.Depth;
                passData.preview = cameraData.cameraType == CameraType.Preview;
                passData.clearColor = cameraData.camera.backgroundColor;
                passData.baseCamera = cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base;
                passData.view = viewMatrix;
                passData.projection = projectionMatrix;
                passData.LowresTargetWidth = lowResConfig.Width;
                passData.LowresTargetHeight = lowResConfig.Height;
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                if (passData.preview)
                {
                    builder.UseTexture(passData.LowResDepth, AccessFlags.Write);
                    builder.UseTexture(passData.LowResColor, AccessFlags.Write);
                }
                else if (Settings.UsingRedirection)
                {
                    builder.UseTexture(passData.LowResDepth, AccessFlags.Write);
                    builder.UseTexture(passData.LowResColor,
                        passData.baseCamera
                            ? AccessFlags.Write
                            : AccessFlags.ReadWrite
                            );
                }
                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(context.cmd, data);
                });
            }
        }


        static void ExecutePass(IUnsafeCommandBuffer command, PassData data)
        {
            if (data.preview)
            {
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                command.SetRenderTarget(data.LowResColor, data.LowResDepth);
                command.ClearRenderTarget(true, true, data.clearColor);
                return;
            }

            if (data.settings.Filter == PixelizationFilter.FullScene)
            {
                //command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                command.SetRenderTarget(data.LowResColor, data.LowResDepth);
                if (data.baseCamera)
                    command.ClearRenderTarget(true, true, data.clearColor);
                else
                    command.ClearRenderTarget(true, false, data.clearColor);
                command.SetViewProjectionMatrices(data.view, data.projection);
                command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, data.view.inverse);
                command.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, data.projection.inverse);
                command.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, 1.0f);
                // Match Unity's screen vectors to the redirected target. _ScaledScreenParams is required for Forward+; see #255.
                float width = data.LowresTargetWidth;
                float height = data.LowresTargetHeight;
                var screenParams = new Vector4(width, height, 1.0f + 1.0f / width, 1.0f + 1.0f / height);
                command.SetGlobalVector(ProPixelizerRedirectedUnityVec4s.SCREEN_PARAMS, screenParams);
                command.SetGlobalVector(ProPixelizerRedirectedUnityVec4s.SCALED_SCREEN_PARAMS, screenParams);
                command.SetGlobalVector(ProPixelizerRedirectedUnityVec4s.SCREEN_SIZE, new Vector4(width, height, 1.0f / width, 1.0f / height));
            }
            else
                ProPixelizerUtils.SetPixelGlobalScale(command, 0.01f); // disable pixel size in other cases - draw objects wont be matching metadata buffer.
        }
    }

    public class ProPixelizerRenderGraphUnRedirectPass : ProPixelizerPass
    {
        readonly static int ScreenParams = Shader.PropertyToID(ProPixelizerRedirectedUnityVec4s.SCREEN_PARAMS);
        readonly static int ScaledScreenParams = Shader.PropertyToID(ProPixelizerRedirectedUnityVec4s.SCALED_SCREEN_PARAMS);
        readonly static int ScreenSize = Shader.PropertyToID(ProPixelizerRedirectedUnityVec4s.SCREEN_SIZE);

        class PassData
        {
            public TextureHandle CameraColor;
            public TextureHandle CameraDepth;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public Rect Viewport;
            public Vector4 ScreenParams;
            public Vector4 ScaledScreenParams;
            public Vector4 ScreenSize;
        }

        public ProPixelizerRenderGraphUnRedirectPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resources = frameData.Get<UniversalResourceData>();
            var originals = frameData.Get<ProPixelizerOriginalCameraTargets>();
            
            if (Settings.Filter == PixelizationFilter.FullScene && cameraData.cameraType != CameraType.Preview)
            {
                // restore original buffers
                resources.cameraColor = originals.Color;
                resources.cameraDepth = originals.Depth;

                var camera = cameraData.camera;
                var originalTargetDescriptor = originals.Color.GetDescriptor(renderGraph);

                float cameraWidth = camera.pixelWidth;
                float cameraHeight = camera.pixelHeight;
                if (cameraData.renderType == CameraRenderType.Overlay)
                {
                    float renderScale = Mathf.Max(cameraData.renderScale, 0.0001f);
                    cameraWidth = cameraData.scaledWidth / renderScale;
                    cameraHeight = cameraData.scaledHeight / renderScale;
                }

                var unityVec4s = ProPixelizerUtils.CalculateRedirectedUnityVec4s(
                    camera,
                    cameraWidth,
                    cameraHeight,
                    originalTargetDescriptor.width,
                    originalTargetDescriptor.height);

                using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerRenderGraphUnRedirectPass), out var passData))
                {
                    passData.CameraColor = originals.Color;
                    passData.CameraDepth = originals.Depth;
                    passData.View = cameraData.GetViewMatrix();
                    passData.Projection = cameraData.GetProjectionMatrix();
                    passData.Viewport = new Rect(0.0f, 0.0f, unityVec4s.ScaledScreenParams.x, unityVec4s.ScaledScreenParams.y);
                    passData.ScreenParams = unityVec4s.ScreenParams;
                    passData.ScaledScreenParams = unityVec4s.ScaledScreenParams;
                    passData.ScreenSize = unityVec4s.ScreenSize;

                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.UseTexture(passData.CameraColor, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.CameraDepth, AccessFlags.ReadWrite);
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePass(context.cmd, data);
                    });
                }
            }
        }

        static void ExecutePass(IUnsafeCommandBuffer command, PassData data)
        {
            command.SetRenderTarget(data.CameraColor, data.CameraDepth);
            command.SetViewport(data.Viewport);
            command.SetViewProjectionMatrices(data.View, data.Projection);
            command.SetGlobalVector(ScreenParams, data.ScreenParams);
            command.SetGlobalVector(ScaledScreenParams, data.ScaledScreenParams);
            command.SetGlobalVector(ScreenSize, data.ScreenSize);
        }
    }
#endif
}
