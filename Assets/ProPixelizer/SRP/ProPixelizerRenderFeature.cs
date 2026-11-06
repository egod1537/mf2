// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using static ProPixelizer.ProPixelizerBlitPass;

namespace ProPixelizer
{

    /// <summary>
    /// The ProPixelizerRenderFeature adds all the passes required for ProPixelizer to your Scriptable render pipeline.
    /// 
    /// The generalised pass structure is as follows (--> Indicates render events).
    /// 
    /// Configure:
    ///   * Low-res targets configured and buffers allocated.
    ///   * ProPixelizerMetadata buffer configured - same target size as low-res target (pixelisation target).
    /// 
    /// --> BeforeRenderingOpaques
    ///   * ProPixelizerMetadata buffer rendered.
    ///   * Outline detection pass performed.
    /// * Render Opaques
    /// --> AfterRenderingOpaques
    ///   * Pixelated objects rendered to the low-res target.
    ///   * ProPixelizer dithered pixelisation applied to low-res target.
    ///   * Low-res target merged back into scene buffer, blended according to depth.
    /// * Onward as usual (render transparents, etc).
    /// 
    /// For 'Entire Screen'
    /// 
    /// --> Configure:
    ///   * Low-res targets configured and buffers allocated.
    ///   * ProPixelizerMetadata buffer configured - same target size as low-res target.
    /// 
    /// --> BeforeRenderingOpaques
    ///   * Change target to low-res target (or, perhaps easier, change size of main target).
    ///   * Outline detection pass performed
    /// --> Render Opaques
    /// --> AfterRenderingOpaqures
    ///   * ProPixelizer dithered pixelisation applied to low-res target.
    ///   * Blend low-res target into scene buffer.
    /// 
    /// </summary>
    public class ProPixelizerRenderFeature : ScriptableRendererFeature
    {
        #region User Properties
        [FormerlySerializedAs("DepthTestOutlines")]
        [Tooltip("Perform depth testing for outlines where object IDs differ. This prevents outlines appearing when one object intersects another, but requires an extra depth sample.")]
        public bool UseDepthTestingForIDOutlines = true;

        [Tooltip("The threshold value used when depth comparing outlines.")]
        public float DepthTestThreshold = 0.0001f;

        [Tooltip("Use normals for edge detection. This will analyse pixelated screen normals to determine where edges occur within an objects silhouette.")]
        public bool UseNormalsForEdgeDetection = true;

        [Tooltip("Threshold value when using normals for detecting edges.")]
        public float NormalEdgeDetectionThreshold = 2f;

        [Tooltip("Perform additional depth testing to confirm edges due to normals.")]
        public bool UseDepthTestingForEdgeOutlines = true;

        [Tooltip("Default scale of the low-resolution render target, used in Scene tab. Game cameras may override this, e.g. to set world space pixel size.")]
        [Range(0.01f, 1.0f)]
        public float DefaultLowResScale = 1.0f;

        [Tooltip("Whether to perform ProPixelizer's 'Pixel Expansion' method of pixelation. This is required for per-object pixelisation and for moving pixelated objects at apparent sub-pixel resolutions.")]
        public bool UsePixelExpansion = true;

        [Tooltip("Selection of layers to draw pixelated.")]
        public LayerMask PixelizedLayers;

        [Tooltip("Prepass configuration ProPixelizer should work with; must match the setup of your Renderer. Note that other RenderFeatures you add modify URP's prepass configuration, e.g. Decals may cause a DepthNormals prepass.")]
        public PipelinePrepassConfiguration PrepassConfiguration = PipelinePrepassConfiguration.AutoDetect;

        [Tooltip("Use pixel expansion in the editor scene tab?")]
        public bool EditorPixelExpansion = true;

        [Tooltip("Generates warnings if the pipeline state is incompatible with ProPixelizer.")]
        public bool GenerateWarnings = true;
        #endregion

        #region Passes

        ProPixelizerPixelizationMapPass _PixelizationMapPass;

        ProPixelizerApplyPixelizationMapToColor _ApplyPixelizationMapToColor;
        ProPixelizerApplyPixelizationMapToDepth _ApplyPixelizationMapToDepth;
        ProPixelizerBlitPass _BlitExpandedColorPass;
        ProPixelizerBlitPass _BlitExpandedDepthPass;
        
        ProPixelizerMetadataPass _MetadataPass;
        ProPixelizerOutlineDetectionPass _OutlineDetectionPass;
        ProPixelizerLowResolutionDrawPass _LowResolutionDrawPass;
        ProPixelizerRecompositeColorPass _RecompositeColorPass;
        ProPixelizerRecompositeDepthPass _RecompositeDepthPass;
        ProPixelizerBlitPass _BlitRecomposedColorPass;
        ProPixelizerBlitPass _BlitRecomposedDepthAttachmentPass;
        ProPixelizerBlitPass _BlitRecomposedDepthTargetPass;
        ProPixelizerLowResolutionConfigurationPass _LowResConfigurationPass;
        ProPixelizerLowResolutionTargetPass _LowResTargetPass;
        ProPixelizerOpaqueDrawRedirectionPass _OpaqueDrawRedirectionPass;
        ProPixelizerUndoRedirectPass _UndoRedirectPass;
#if UNITY_2023_3_OR_NEWER
        ProPixelizerRenderGraphRedirectPass _GraphRedirectsPass;
        ProPixelizerRenderGraphUnRedirectPass _GraphUnredirectsPass;
        ProPixelizerStoreOriginalCameraTargetsPass _StoreOriginalTargetsPass;
        ProPixelizerRGImportSubcameraTargetsPass _RGImportSubcameraTargetsPass;
#endif
        ProPixelizerFullscreenColorGradingPass _FullscreenColorGradingPass;
        SetLowResProjectionMatricesPass _SetLowResProjectionMatricesForTransparentsPass;
        ProPixelizerExportSubcameraDepthPass _CopySubcameraDepthPass;
        ProPixelizerExportSubcameraColorPass _CopySubcameraColorPass;
        ProPixelizerSetPassVisibilityPass _HideProPixelizerShadersForOpaquesPass;
        ProPixelizerSetPixelScaleForPrepassPass _SetPixelScaleForPrepass;
        ProPixelizerSetPassVisibilityPass _ShowProPixelizerShadersForPrepasses;
        ProPixelizerSetPassVisibilityPass _HideProPixelizerShadersForPrepasses;
        ProPixelizerBlitPass _SubcameraCopyPrepassDepthIntoDepthAttachment;
        ProPixelizerStoreSceneDepthPass _StoreSceneDepthPass;

        #endregion

        public override void Create()
        {
            LoadShaderResources();
            
            _LowResConfigurationPass = new ProPixelizerLowResolutionConfigurationPass();
            _LowResTargetPass = new ProPixelizerLowResolutionTargetPass(_LowResConfigurationPass);
            _LowResolutionDrawPass = new ProPixelizerLowResolutionDrawPass(_LowResConfigurationPass, _LowResTargetPass);
            _MetadataPass = new ProPixelizerMetadataPass(_LowResConfigurationPass);
            _OutlineDetectionPass = new ProPixelizerOutlineDetectionPass(Shaders, _LowResConfigurationPass, _MetadataPass);
            _OpaqueDrawRedirectionPass = new ProPixelizerOpaqueDrawRedirectionPass(_LowResConfigurationPass, _LowResTargetPass);
            _UndoRedirectPass = new ProPixelizerUndoRedirectPass();

            _PixelizationMapPass = new ProPixelizerPixelizationMapPass(Shaders,
                _MetadataPass,
                _LowResConfigurationPass,
                _LowResTargetPass
                );
            _ApplyPixelizationMapToColor = new ProPixelizerApplyPixelizationMapToColor(
                Shaders,
                _MetadataPass,
                _LowResConfigurationPass,
                _LowResTargetPass,
                _PixelizationMapPass);
            _ApplyPixelizationMapToDepth = new ProPixelizerApplyPixelizationMapToDepth(
                Shaders,
                _MetadataPass,
                _LowResConfigurationPass,
                _LowResTargetPass,
                _PixelizationMapPass);
            // Blit targets will be defined below.
            _BlitExpandedColorPass = new ProPixelizerBlitPass(Shaders, source: BlittableHandleType.ProPixelizerColor, sourcePass: _ApplyPixelizationMapToColor);
            _BlitExpandedDepthPass = new ProPixelizerBlitPass(Shaders, source: BlittableHandleType.ProPixelizerDepth, sourcePass: _ApplyPixelizationMapToDepth);

            _StoreSceneDepthPass = new ProPixelizerStoreSceneDepthPass(Shaders, _LowResTargetPass);
            _RecompositeColorPass = new ProPixelizerRecompositeColorPass(
                Shaders,
                _LowResTargetPass,
                _StoreSceneDepthPass,
                _MetadataPass,
                _ApplyPixelizationMapToDepth);
            _RecompositeDepthPass = new ProPixelizerRecompositeDepthPass(
                Shaders,
                _LowResTargetPass,
                _StoreSceneDepthPass,
                _MetadataPass,
                _ApplyPixelizationMapToDepth);
            _BlitRecomposedColorPass = new ProPixelizerBlitPass(Shaders,
                source: BlittableHandleType.ProPixelizerColor,
                target: BlittableHandleType.CameraColor,
                sourcePass: _RecompositeColorPass);
            _BlitRecomposedDepthAttachmentPass = new ProPixelizerBlitPass(
                Shaders,
                source: BlittableHandleType.ProPixelizerDepth,
                target: BlittableHandleType.CameraDepthAttachment
                );
            _BlitRecomposedDepthTargetPass = new ProPixelizerBlitPass(
                Shaders,
                source: BlittableHandleType.ProPixelizerDepth,
                target: BlittableHandleType.CameraDepthTexture,
                sourcePass: _RecompositeDepthPass
                );
            _FullscreenColorGradingPass = new ProPixelizerFullscreenColorGradingPass(Shaders, _MetadataPass, _LowResConfigurationPass, _LowResTargetPass);
            _SetLowResProjectionMatricesForTransparentsPass = new SetLowResProjectionMatricesPass(RenderPassEvent.BeforeRenderingTransparents, _LowResConfigurationPass);
#if UNITY_2023_3_OR_NEWER
            _GraphRedirectsPass = new ProPixelizerRenderGraphRedirectPass();
            _GraphUnredirectsPass = new ProPixelizerRenderGraphUnRedirectPass();
            _StoreOriginalTargetsPass = new ProPixelizerStoreOriginalCameraTargetsPass();
            _RGImportSubcameraTargetsPass = new ProPixelizerRGImportSubcameraTargetsPass();
#endif
            _CopySubcameraDepthPass = new ProPixelizerExportSubcameraDepthPass(Shaders);
            _CopySubcameraColorPass = new ProPixelizerExportSubcameraColorPass();
            _HideProPixelizerShadersForOpaquesPass = new ProPixelizerSetPassVisibilityPass(RenderPassEvent.BeforeRenderingOpaques, false);
            _ShowProPixelizerShadersForPrepasses = new ProPixelizerSetPassVisibilityPass(RenderPassEvent.BeforeRenderingPrePasses, true);
            _HideProPixelizerShadersForPrepasses = new ProPixelizerSetPassVisibilityPass(RenderPassEvent.BeforeRenderingPrePasses, false);
            _SetPixelScaleForPrepass = new ProPixelizerSetPixelScaleForPrepassPass();
            _SubcameraCopyPrepassDepthIntoDepthAttachment = new ProPixelizerBlitPass(
                Shaders,
                source: BlittableHandleType.CameraDepthTexture,
                target: BlittableHandleType.CameraDepthAttachment
                );
            
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // NB: We don't want Depth input - we actually want to modify CameraDepthAttachment, which we can do at any time,
            // and then have the CopyDepth into CameraDepthTarget after we are done.
            //_LowResRecompositionPass.ConfigureInput(ScriptableRenderPassInput.Color);
            //_FinalRecompositionPass.ConfigureInput(ScriptableRenderPassInput.Color);

#if UNITY_2023_3_OR_NEWER
            _GraphRedirectsPass._Renderer = renderer as UniversalRenderer;
#endif

            var settings = new ProPixelizerSettings
            {
                UseDepthTestingForEdgeOutlines = UseDepthTestingForEdgeOutlines,
                UseDepthTestingForIDOutlines = UseDepthTestingForIDOutlines,
                DepthTestThreshold = DepthTestThreshold,

                UseNormalsForEdgeDetection = UseNormalsForEdgeDetection,
                NormalEdgeDetectionThreshold = NormalEdgeDetectionThreshold,

                Filter = PixelizationFilter.OnlyProPixelizer,
                PixelizedLayers = PixelizedLayers,
                UsePixelExpansion = UsePixelExpansion,

                DefaultLowResScale = DefaultLowResScale,
                FullscreenLUT = null,
                UsePixelArtUpscalingFilter = true,
                RenderingPathway = ProPixelizerRenderingPathway.MainWithVirtualCamera,
                Enabled = true,
                PrepassConfiguration = PrepassConfiguration
            };

            // Camera ProPixelizer overrides.
            var proPixelizerCamera = renderingData.cameraData.camera.GetComponent<ProPixelizerCamera>();
            if (proPixelizerCamera != null)
                proPixelizerCamera.ModifySettings(ref settings);

            var subcamera = renderingData.cameraData.camera.GetComponent<ProPixelizerSubCamera>();
            if (subcamera != null)
                subcamera.ModifySettings(ref settings);

            ModifySettings(ref settings, renderer, ref renderingData);

            // Configure passes
            _LowResConfigurationPass.ConfigureForSettings(settings);
            _LowResTargetPass.ConfigureForSettings(settings);
            _MetadataPass.ConfigureForSettings(settings);
            _OutlineDetectionPass.ConfigureForSettings(settings);
            _OpaqueDrawRedirectionPass.ConfigureForSettings(settings);
            _UndoRedirectPass.ConfigureForSettings(settings);
            _LowResolutionDrawPass.ConfigureForSettings(settings);
            _PixelizationMapPass.ConfigureForSettings(settings);
            _ApplyPixelizationMapToColor.ConfigureForSettings(settings);
            _ApplyPixelizationMapToDepth.ConfigureForSettings(settings);
            _BlitExpandedColorPass.ConfigureForSettings(settings);
            _BlitExpandedDepthPass.ConfigureForSettings(settings);
            _RecompositeColorPass.ConfigureForSettings(settings);
            _RecompositeDepthPass.ConfigureForSettings(settings);
            _FullscreenColorGradingPass.ConfigureForSettings(settings);
            _SetLowResProjectionMatricesForTransparentsPass.ConfigureForSettings(settings);
            _BlitRecomposedColorPass.ConfigureForSettings(settings);
            _BlitRecomposedDepthAttachmentPass.ConfigureForSettings(settings);
            _BlitRecomposedDepthTargetPass.ConfigureForSettings(settings);
            _CopySubcameraDepthPass.ConfigureForSettings(settings);
            _CopySubcameraColorPass.ConfigureForSettings(settings);
            _HideProPixelizerShadersForOpaquesPass.ConfigureForSettings(settings);
            _ShowProPixelizerShadersForPrepasses.ConfigureForSettings(settings);
            _HideProPixelizerShadersForPrepasses.ConfigureForSettings(settings);
            _SetPixelScaleForPrepass.ConfigureForSettings(settings);
            _SubcameraCopyPrepassDepthIntoDepthAttachment.ConfigureForSettings(settings);
            _StoreSceneDepthPass.ConfigureForSettings(settings);
#if UNITY_2023_3_OR_NEWER
            _GraphRedirectsPass.ConfigureForSettings(settings);
            _GraphUnredirectsPass.ConfigureForSettings(settings);
            _StoreOriginalTargetsPass.ConfigureForSettings(settings);
            _RGImportSubcameraTargetsPass.ConfigureForSettings(settings);
#endif
            

            bool usingRenderGraphPathway = ProPixelizerUtils.PipelineUsingRG();

            _FullscreenColorGradingPass.renderPassEvent = settings.ColorGradingEvent;
            _RecompositeColorPass.renderPassEvent = settings.RecompositeColorEvent;
            _RecompositeDepthPass.renderPassEvent = settings.RecompositeDepthEvent;
            _BlitRecomposedColorPass.renderPassEvent = settings.CopyRecomposedColorEvent;
            _BlitRecomposedDepthAttachmentPass.renderPassEvent = settings.CopyRecomposedDepthAttachmentEvent;
            _BlitRecomposedDepthTargetPass.renderPassEvent = settings.CopyRecomposedDepthTargetEvent;
            _UndoRedirectPass.renderPassEvent = settings.RecompositeColorEvent;
            switch (settings.GetRecomposedDepthAttachmentSource(usingRenderGraphPathway))
            {
                default:
                case RecomposedDepthAttachmentSourceMode.PixelExpandedDepth:
                    _BlitRecomposedDepthAttachmentPass.SourcePass = _ApplyPixelizationMapToDepth;
                    break;
                case RecomposedDepthAttachmentSourceMode.RecompositedDepth:
                    _BlitRecomposedDepthAttachmentPass.SourcePass = _RecompositeDepthPass;
                    break;
            }

            _ApplyPixelizationMapToDepth.renderPassEvent = settings.ApplyPixelizationToDepthAttachmentEvent;
            _ApplyPixelizationMapToColor.renderPassEvent = settings.ApplyPixelizationToColorEvent;
            _PixelizationMapPass.renderPassEvent = settings.PixelizationMapEvent;
            _BlitExpandedDepthPass.renderPassEvent = settings.BlitExpandedDepthEvent;
            _BlitExpandedColorPass.renderPassEvent = settings.ApplyPixelizationToColorEvent;

            // Configure Pixel expansion blits
            if (settings.TargetForPixelation == TargetForPixelation.ProPixelizerTarget)
            {
                _BlitExpandedColorPass.Target = BlittableHandleType.ProPixelizerColor;
                _BlitExpandedDepthPass.Target = BlittableHandleType.ProPixelizerDepth;
                _BlitExpandedDepthPass.TargetPass = _LowResTargetPass;
                _BlitExpandedColorPass.TargetPass = _LowResTargetPass;
            }
            else
            {
                _BlitExpandedColorPass.Target = BlittableHandleType.CameraColor;
                _BlitExpandedDepthPass.Target =
                    settings.UsingDepthPrepass
                        ? BlittableHandleType.CameraDepthTexture
                        : BlittableHandleType.CameraDepthAttachment;
            }

            renderer.EnqueuePass(_LowResConfigurationPass);
            renderer.EnqueuePass(_LowResTargetPass);
            if (!settings.Enabled) return; // we always run the first two passes, so that global float keywords can be set.

            renderer.EnqueuePass(_SetPixelScaleForPrepass);
            
            if (settings.IsStoringOriginalSceneDepthRequired)
            {
                _StoreSceneDepthPass.renderPassEvent = settings.RecompositeDepthEvent; // scheduled immediately before depth recomposition
                renderer.EnqueuePass(_StoreSceneDepthPass);
            }

            if (settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera && !settings.UsePixelExpansion)
                renderer.EnqueuePass(_ShowProPixelizerShadersForPrepasses);
            else
                renderer.EnqueuePass(_HideProPixelizerShadersForPrepasses);

            if (
                settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera ||
                settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera
                )
            {
                renderer.EnqueuePass(_MetadataPass);
                renderer.EnqueuePass(_OutlineDetectionPass);

                // ...opaque draw happens between these...

                if (settings.Filter != PixelizationFilter.FullScene)
                    renderer.EnqueuePass(_LowResolutionDrawPass);
                if (settings.UsePixelExpansion)
                {
                    renderer.EnqueuePass(_PixelizationMapPass);
                    renderer.EnqueuePass(_ApplyPixelizationMapToColor);
                    renderer.EnqueuePass(_BlitExpandedColorPass);
                    renderer.EnqueuePass(_ApplyPixelizationMapToDepth);
                    if (settings.ShouldBlitExpandedDepth)
                        renderer.EnqueuePass(_BlitExpandedDepthPass);
                }
            }

#if UNITY_2023_3_OR_NEWER
            if (usingRenderGraphPathway)
                renderer.EnqueuePass(_StoreOriginalTargetsPass);
#endif

            // Redirection only for virtual camera setup
            if (settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera)
            {
                if (usingRenderGraphPathway)
                {
#if UNITY_2023_3_OR_NEWER
                    renderer.EnqueuePass(_GraphRedirectsPass); // RG only
                    renderer.EnqueuePass(_GraphUnredirectsPass); // RG only
#endif
                }
                else
                {
                    renderer.EnqueuePass(_OpaqueDrawRedirectionPass); // non-RG only
                    if (settings.Filter == PixelizationFilter.FullScene)
                        renderer.EnqueuePass(_SetLowResProjectionMatricesForTransparentsPass);
                }
            }

            if (
                settings.Filter != PixelizationFilter.FullScene && renderingData.cameraData.cameraType != CameraType.Preview
                && (settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera || settings.RenderingPathway == ProPixelizerRenderingPathway.MainCamera)
                )
                renderer.EnqueuePass(_HideProPixelizerShadersForOpaquesPass);

            if (
                settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera ||
                settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera
                )
                renderer.EnqueuePass(_FullscreenColorGradingPass);

            // Recomposition stage
            if (
                settings.RenderingPathway == ProPixelizerRenderingPathway.MainWithVirtualCamera ||
                settings.RenderingPathway == ProPixelizerRenderingPathway.MainCamera
                )
            {
                renderer.EnqueuePass(_RecompositeColorPass);
                renderer.EnqueuePass(_RecompositeDepthPass);
                renderer.EnqueuePass(_BlitRecomposedColorPass);
                if (!usingRenderGraphPathway && settings.UsingRedirection)
                    renderer.EnqueuePass(_UndoRedirectPass);
                // In compatibility mode, when redirected+prepassed then low-res depth is already correct after opaques.
                // - When expansion is off, it's just the rendered objects.
                // - When expansion is on the _BlitExpandedDepthPass is already blitting into LowRes_Depth.
                bool shouldCopyDepthToFinalCameraAttachment =
                    !(
                        usingRenderGraphPathway
                        && settings.UsingRedirection
                        && settings.UsingDepthPrepass
                    );
                if (shouldCopyDepthToFinalCameraAttachment)
                    renderer.EnqueuePass(_BlitRecomposedDepthAttachmentPass);
                renderer.EnqueuePass(_BlitRecomposedDepthTargetPass);
            }

            if (settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera)
            {
                renderer.EnqueuePass(_CopySubcameraDepthPass);
                renderer.EnqueuePass(_CopySubcameraColorPass);
                if (settings.PrepassConfiguration != PipelinePrepassConfiguration.None)
                    renderer.EnqueuePass(_SubcameraCopyPrepassDepthIntoDepthAttachment);
            }
            if (usingRenderGraphPathway && settings.RenderingPathway == ProPixelizerRenderingPathway.MainCamera)
            {
#if UNITY_2023_3_OR_NEWER
                renderer.EnqueuePass(_RGImportSubcameraTargetsPass);
#endif
            }

            if (GenerateWarnings)
                ProPixelizerVerification.GenerateWarnings();
        }

        public void ModifySettings(ref ProPixelizerSettings settings, ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera.cameraType == CameraType.SceneView && !EditorPixelExpansion)
                settings.UsePixelExpansion = false;

            if (renderingData.cameraData.camera.cameraType == CameraType.Preview && !ProPixelizerUtils.PipelineUsingRG())
            {
                settings.UsePixelExpansion = false;
                settings.Filter = PixelizationFilter.OnlyProPixelizer;
                settings.RenderingPathway = ProPixelizerRenderingPathway.MainWithVirtualCamera;
            }

            if (renderingData.cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay) {
                if (settings.Filter == PixelizationFilter.FullScene)
                {
                    settings.Filter = PixelizationFilter.OnlyProPixelizer;
                    settings.UsePixelArtUpscalingFilter = false;
                }
            }

            if (settings.PrepassConfiguration == PipelinePrepassConfiguration.AutoDetect)
                settings.PrepassConfiguration = ProPixelizerUtils.GuessPrepassConfiguration((UniversalRenderer)renderer, ref renderingData);

#if UNITY_2023_3_OR_NEWER
            // trying to get the fullscreen pass working with the backbuffer was a nightmare.
            // we don't need fullscreen mode in preview windows anyway, and layer setup isn't used either.
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview && ProPixelizerUtils.PipelineUsingRG()) {
                settings.Filter = PixelizationFilter.OnlyProPixelizer;
                settings.RenderingPathway = ProPixelizerRenderingPathway.MainWithVirtualCamera;
                settings.PrepassConfiguration = PipelinePrepassConfiguration.None;
            }
#endif

            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
                settings.UsePixelArtUpscalingFilter = false;

            if (!settings.Enabled)
                settings.UsePixelExpansion = false;
        }

#if !PRERG_API_REMOVED
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            // Note: Preview camera does not call SetupRenderPasses (bug) - https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
            _ApplyPixelizationMapToColor.ConfigureInput(ScriptableRenderPassInput.Color);
            _OpaqueDrawRedirectionPass.FindDrawObjectPasses(renderer as UniversalRenderer, in renderingData);
        }
#endif

        protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
#if !PRERG_API_REMOVED
                _LowResTargetPass.Dispose();
                _MetadataPass.Dispose();
                _OutlineDetectionPass.Dispose();
                _PixelizationMapPass.Dispose();
                _ApplyPixelizationMapToColor.Dispose();
                _ApplyPixelizationMapToDepth.Dispose();
                _RecompositeColorPass.Dispose();
                _RecompositeDepthPass.Dispose();
                _FullscreenColorGradingPass.Dispose();
                _StoreSceneDepthPass.Dispose();
#endif
                _LowResolutionDrawPass.Dispose();
            }
    }

        #region Serialized Shader Fields
        // We serialize shader fields to prevent them from being stripped from builds.
        [HideInInspector, SerializeField]
        ShaderResources Shaders;

        private void LoadShaderResources()
        {
            Shaders = new ShaderResources().Load();
        }
        #endregion
    }
};
