// Copyright Elliot Bentine, 2018-
using System;
using System.Linq.Expressions;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace ProPixelizer
{
    /// <summary>
    /// A set of rendering utility functions to help me implement functionality in a Unity Engine Version agnostic way.
    /// </summary>
    public static class ProPixelizerUtils {

        public struct RedirectedUnityVec4s
        {
            public Vector4 ScreenParams;
            public Vector4 ScaledScreenParams;
            public Vector4 ScreenSize;
        }

        public static RedirectedUnityVec4s CalculateRedirectedUnityVec4s(
            Camera camera,
            float screenWidth,
            float screenHeight,
            float scaledTargetWidth,
            float scaledTargetHeight)
        {
            if (camera.allowDynamicResolution)
            {
                scaledTargetWidth *= ScalableBufferManager.widthScaleFactor;
                scaledTargetHeight *= ScalableBufferManager.heightScaleFactor;
            }

            screenWidth = Mathf.Max(1.0f, screenWidth);
            screenHeight = Mathf.Max(1.0f, screenHeight);
            scaledTargetWidth = Mathf.Max(1.0f, scaledTargetWidth);
            scaledTargetHeight = Mathf.Max(1.0f, scaledTargetHeight);

            return new RedirectedUnityVec4s
            {
                ScreenParams = new Vector4(screenWidth, screenHeight, 1.0f + 1.0f / screenWidth, 1.0f + 1.0f / screenHeight),
                ScaledScreenParams = new Vector4(scaledTargetWidth, scaledTargetHeight, 1.0f + 1.0f / scaledTargetWidth, 1.0f + 1.0f / scaledTargetHeight),
                ScreenSize = new Vector4(scaledTargetWidth, scaledTargetHeight, 1.0f / scaledTargetWidth, 1.0f / scaledTargetHeight)
            };
        }

        public static bool PipelineUsingRG()
        {
#if UNITY_6000_4_OR_NEWER
            return true;
#else
            #if UNITY_6000_0_OR_NEWER
                return !GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
            #else
                return false;
            #endif
#endif
        }

        /// <summary>
        /// True when the legacy pre-RenderGraph pathway is active.
        /// </summary>
        public static bool PipelineUsingPreRGPathway()
        {
#if PRERG_API_REMOVED
            return false;
#elif UNITY_6000_0_OR_NEWER
            return GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode;
#else
            return true;
#endif
        }

        public static void SetCameraMatrices(CommandBuffer cmd, ref CameraData cameraData)
        {
#if CAMERADATA_MATRICES
            cmd.SetViewMatrix(cameraData.GetViewMatrix());
            cmd.SetProjectionMatrix(cameraData.GetProjectionMatrix());
#else
            cmd.SetViewMatrix(cameraData.camera.worldToCameraMatrix);
            cmd.SetProjectionMatrix(cameraData.camera.projectionMatrix);
#endif
        }

#if UNITY_2023_3_OR_NEWER
        public static void SetPixelGlobalScale(IRasterCommandBuffer buffer, float scale)
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PIXEL_SCALE, scale);
        }
#endif

        public static void SetPixelGlobalScale(CommandBuffer buffer, float scale)
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PIXEL_SCALE, scale);
        }

        /// <summary>
        /// Calculates view and projection matrices for the low resolution targets.
        /// </summary>
        public static void CalculateLowResViewProjection(
            in Matrix4x4 cameraView, in Matrix4x4 cameraProjection,
            bool ortho, float nearClip, float farClip,
            float OrthoWidth, float OrthoHeight, Vector3 LowResCameraDeltaWS,
            float LowResPerspectiveVerticalFOV, float aspect,
            bool forSubcamera,
            out Matrix4x4 view, out Matrix4x4 projection
            )
        {
            if (forSubcamera)
            {
                projection = cameraProjection;
                view = cameraView;
                return;
            }

            if (ortho)
                projection = Matrix4x4.Ortho(
                    -OrthoWidth / 2f, OrthoWidth / 2f,
                    -OrthoHeight / 2f, OrthoHeight / 2f,
                    nearClip, farClip
                    );
            else
                projection = Matrix4x4.Perspective(LowResPerspectiveVerticalFOV, aspect, nearClip, farClip);
            
            view = cameraView * Matrix4x4.Translate(LowResCameraDeltaWS);
        }

        /// <summary>
        /// Sets view and projection matrices used to render to the low-res target.
        /// </summary>
        public static void SetLowResViewProjectionMatrices(
            CommandBuffer cmd,
            ref CameraData cameraData,
            float OrthoWidth,
            float OrthoHeight,
            Vector3 LowResCameraDeltaWS,
            float LowResPerspectiveVerticalFOV,
            float aspect,
            bool forSubcamera
            )
        {
            var camera = cameraData.camera;
            CalculateLowResViewProjection(
                cameraData.GetViewMatrix(),
                cameraData.GetProjectionMatrix(),
                camera.orthographic,
                camera.nearClipPlane, camera.farClipPlane,
                OrthoWidth, OrthoHeight,
                LowResCameraDeltaWS,
                LowResPerspectiveVerticalFOV, aspect,
                forSubcamera,
                out var viewMatrix, out var projectionMatrix
                );

            cmd.SetViewMatrix(viewMatrix);
            cmd.SetProjectionMatrix(projectionMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW, viewMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION, projectionMatrix);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, viewMatrix.inverse);
            cmd.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, projectionMatrix.inverse);
        }

        /// <summary>
        /// Size of the margin around the visible low-res pixel target, in pixels.
        /// </summary>
        public const int LOW_RESOLUTION_TARGET_MARGIN = 5;

        /// <summary>
        /// Calculates a low resolution target size
        /// </summary>
        /// <param name="cameraWidth">width of the camera render texture in pixels</param>
        /// <param name="cameraHeight">height of the camera render texture in pixels</param>
        /// <param name="scale">resolution scale of the low res target. Values < 1 are pixelated.</param>
        /// <param name="width">output width in pixels</param>
        /// <param name="height">output height in pixels</param>
        public static void CalculateLowResTargetSize(int cameraWidth, int cameraHeight, float scale, out int width, out int height, bool withMargin = true)
        {
            // see matching define in ScreenUtils.hlsl
            width = Mathf.CeilToInt(cameraWidth * scale);// + 2;
            height = Mathf.CeilToInt(cameraHeight * scale);// + 2;
            //force odd dimensions by /2*2+1
            width = 2 * (width / 2) + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0);
            height = 2 * (height / 2) + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0);

            width = Mathf.Min(width, cameraWidth + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0));
            height = Mathf.Min(height, cameraHeight + 1 + (withMargin ? 2 * LOW_RESOLUTION_TARGET_MARGIN : 0));
        }

        /// <summary>
        /// Utility functions for getting and setting the Filter Settings on a DrawObjectsPass without boxing or unboxing (avoids alloc).
        /// </summary>
        public static class RendererDrawObjectFilterSettingsUtil
        {
            public readonly static Func<DrawObjectsPass, FilteringSettings> GetDrawPassFilterSettings;
            public readonly static Action<DrawObjectsPass, FilteringSettings> SetDrawPassFilterSettings;
            static RendererDrawObjectFilterSettingsUtil()
            {
                var fieldInfo = typeof(DrawObjectsPass).GetField("m_FilteringSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                var declaringType = fieldInfo.DeclaringType;
                var instanceParam = Expression.Parameter(declaringType, "instance");
                var fieldAccess = Expression.Field(instanceParam, fieldInfo);
                var valueParam = Expression.Parameter(typeof(FilteringSettings), "value");
                GetDrawPassFilterSettings = Expression.Lambda<Func<DrawObjectsPass, FilteringSettings>>(fieldAccess, instanceParam).Compile();
                SetDrawPassFilterSettings = Expression.Lambda<Action<DrawObjectsPass, FilteringSettings>>(Expression.Assign(fieldAccess, valueParam), instanceParam, valueParam).Compile();
            }
        }

#if UNITY_EDITOR
        public static readonly string USER_GUIDE_URL = Universal.ShaderGraph.ProPixelizerURPConstants.USER_GUIDE_URL;
#endif

        static class PrepassAccessors
        {
            static readonly Func<ScriptableRenderer, List<ScriptableRenderPass>> ActiveRenderPassQueueGetter = CreateActiveRenderPassQueueGetter();
            static readonly Func<UniversalRenderer, CopyDepthMode> CopyDepthModeGetter = CreateCopyDepthModeGetter();

            internal static List<ScriptableRenderPass> GetActiveRenderPassQueue(UniversalRenderer renderer) =>
                ActiveRenderPassQueueGetter(renderer);

            internal static CopyDepthMode GetCopyDepthMode(UniversalRenderer renderer) => CopyDepthModeGetter(renderer);

            static Func<ScriptableRenderer, List<ScriptableRenderPass>> CreateActiveRenderPassQueueGetter()
            {
                var property = typeof(ScriptableRenderer).GetProperty("activeRenderPassQueue", BindingFlags.NonPublic | BindingFlags.Instance);
                var getter = property.GetGetMethod(true);
                return (Func<ScriptableRenderer, List<ScriptableRenderPass>>)Delegate.CreateDelegate(
                    typeof(Func<ScriptableRenderer, List<ScriptableRenderPass>>), getter);
            }

            static Func<UniversalRenderer, CopyDepthMode> CreateCopyDepthModeGetter()
            {
                var field = typeof(UniversalRenderer).GetField("m_CopyDepthMode", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                    return _ => CopyDepthMode.AfterTransparents;

                // FieldInfo.GetValue boxes the enum on every camera. Compile a typed getter once.
                var renderer = Expression.Parameter(typeof(UniversalRenderer), "renderer");
                var fieldAccess = Expression.Field(renderer, field);
                return Expression.Lambda<Func<UniversalRenderer, CopyDepthMode>>(fieldAccess, renderer).Compile();
            }
        }

        static bool CanCopyDepth(ref RenderingData data)
        {
            /// Matches URP's implementation as closely as we can across different supported engine versions.
            bool msaaEnabled = data.cameraData.cameraTargetDescriptor.msaaSamples > 1;
            bool supportsDepthCopy = !msaaEnabled &&
                (RenderingUtils.SupportsRenderTextureFormat(RenderTextureFormat.Depth) ||
                 SystemInfo.copyTextureSupport != CopyTextureSupport.None);
            bool msaaDepthResolve = msaaEnabled && SystemInfo.supportsMultisampledTextures != 0;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && msaaDepthResolve)
                return false;
#if !UNITY_6000_0_OR_NEWER
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 && msaaDepthResolve)
                return false;
#endif

            return supportsDepthCopy || msaaDepthResolve;
        }

        static bool UsesDepthPriming(UniversalRenderer renderer, ref RenderingData data, bool requiresDepth, bool canCopyDepth)
        {
#if UNITY_ANDROID || UNITY_IOS || UNITY_TVOS || (UNITY_6000_0_OR_NEWER && UNITY_EMBEDDED_LINUX)
            const bool depthPrimingRecommended = false;
#else
            const bool depthPrimingRecommended = true;
#endif
            bool requested = renderer.depthPrimingMode == DepthPrimingMode.Forced ||
                (depthPrimingRecommended && renderer.depthPrimingMode == DepthPrimingMode.Auto);
            if (!requested)
                return false;

#if UNITY_EDITOR
            if (data.cameraData.isSceneViewCamera)
            {
                var sceneViews = UnityEditor.SceneView.sceneViews;
                for (int i = 0; i < sceneViews.Count; i++)
                {
                    var sceneView = sceneViews[i] as UnityEditor.SceneView;
                    if (sceneView != null && sceneView.camera == data.cameraData.camera &&
                        sceneView.cameraMode.drawMode == UnityEditor.DrawCameraMode.Wireframe)
                        return false;
                }
            }
#endif

#if UNITY_6000_0_OR_NEWER
            if (requiresDepth && !canCopyDepth)
                return false;
#else
            if (!canCopyDepth)
                return false;
#endif

            var cameraData = data.cameraData;
            bool isOffscreenDepthTexture = cameraData.targetTexture != null &&
                cameraData.targetTexture.format == RenderTextureFormat.Depth;

#if PLATFORM_WEBGL && UNITY_6000_0_OR_NEWER
            return false;
#else
    #if PLATFORM_WEBGL
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
                return false;
    #endif
            // ProPixelizer supports Forward and Forward+, so URP's deferred-lighting check is unnecessary here.
            bool canPrime = (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth) &&
                !isOffscreenDepthTexture;
    #if UNITY_6000_0_OR_NEWER
            return canPrime && cameraData.cameraTargetDescriptor.msaaSamples == 1;
    #else
            return canPrime && cameraData.cameraType != CameraType.Reflection;
    #endif
#endif
        }
        /// <summary>
        /// Guesses the prepass configuration from the available data in the pipeline's rendering data struct.
        /// 
        /// Should be called from AddRendererPasses at the _bottom_ of the render feature stack.
        /// URP can add internal requirements after renderer features are enqueued.
        /// </summary>
        public static PipelinePrepassConfiguration GuessPrepassConfiguration(UniversalRenderer renderer, ref RenderingData data)
        {
            RenderPassEvent earliestDepthRequest = (RenderPassEvent)int.MaxValue;
            RenderPassEvent earliestNormalsRequest = (RenderPassEvent)int.MaxValue;
            foreach (var pass in PrepassAccessors.GetActiveRenderPassQueue(renderer))
            {
                if ((pass.input & ScriptableRenderPassInput.Depth) != 0)
                    earliestDepthRequest = (RenderPassEvent)Math.Min((int)earliestDepthRequest, (int)pass.renderPassEvent);
                if ((pass.input & ScriptableRenderPassInput.Normal) != 0)
                    earliestNormalsRequest = (RenderPassEvent)Math.Min((int)earliestNormalsRequest, (int)pass.renderPassEvent);
            }

            if (data.cameraData.requiresDepthTexture)
            {
                CopyDepthMode copyDepthMode = PrepassAccessors.GetCopyDepthMode(renderer);
                RenderPassEvent cameraDepthEvent = RenderPassEvent.AfterRenderingTransparents;
                if (copyDepthMode == CopyDepthMode.ForcePrepass)
                    cameraDepthEvent = RenderPassEvent.AfterRenderingPrePasses;
                else if (copyDepthMode == CopyDepthMode.AfterOpaques)
                    cameraDepthEvent = RenderPassEvent.AfterRenderingOpaques;
                earliestDepthRequest = (RenderPassEvent)Math.Min((int)earliestDepthRequest, (int)cameraDepthEvent);
            }

            if (data.cameraData.postProcessEnabled && data.cameraData.postProcessingRequiresDepthTexture)
            {
                earliestDepthRequest = (RenderPassEvent)Math.Min((int)earliestDepthRequest, (int)RenderPassEvent.BeforeRenderingPostProcessing);
            }

            bool requiresDepth = earliestDepthRequest != (RenderPassEvent)int.MaxValue;
            bool requiresNormals = earliestNormalsRequest != (RenderPassEvent)int.MaxValue;
            bool canCopyDepth = CanCopyDepth(ref data);
            bool requiresTexturePrepass = requiresNormals ||
                (requiresDepth && (!canCopyDepth || earliestDepthRequest < RenderPassEvent.AfterRenderingOpaques));

            bool usesDepthPriming = UsesDepthPriming(renderer, ref data, requiresDepth, canCopyDepth);

            if (!requiresTexturePrepass && !usesDepthPriming)
                return PipelinePrepassConfiguration.None;
            return requiresNormals ? PipelinePrepassConfiguration.DepthNormals : PipelinePrepassConfiguration.Depth;
        }
    }

    class ProPixelizerException : Exception {
        public ProPixelizerException(string message) : base(message) { }
    }
}
