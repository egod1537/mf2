// Copyright Elliot Bentine, 2018-
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// The ProPixelizerCamera performs all functionality required for ProPixelizer:
    ///  - (i) ProPixelizerCamera handles all snapping of cameras and objects to eliminate pixel creep.
    ///  - (ii) Handles resizing of the camera viewport to maintain desired world-space pixel size.
    ///  - (iii) Calculates deltas of the low-res target to enable sub-pixel camera movement in the low-res target.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("ProPixelizer/ProPixelizerCamera")]
    [ExecuteAlways]
    public class ProPixelizerCamera : MonoBehaviour
    { 
        public enum PixelSizeMode
        {
            [Tooltip("The size of a low-res pixel is defined in world-space units.")]
            WorldSpacePixelSize,
            [Tooltip("The resolution of the low-res target is a fixed multiple of the viewport resolution.")]
            FixedDownscalingRatio,
            [Tooltip("The low-resolution target has a fixed resolution.")]
            FixedTargetResolution
        }

        Camera _camera;
        public ProPixelizerSubCamera SubCamera;
        private SceneObjectSnapManager _SnapManager;

        [Tooltip("These layers will not be drawn by the subcamera.")]
        public LayerMask SubcameraExcludedLayers = new LayerMask { value = (1 << 5) | (1 << 1) }; // UI is layer 5 by default, TransparentFx layer 1.

        /// <summary>
        /// Returns true if there is a valid SubCamera. This does _not_ check if the SubCamera is valid.
        /// </summary>
        public bool HasSubCamera => SubCamera != null && SubCamera.transform.parent == transform;
        public bool HasActiveSubCamera => HasSubCamera && SubCamera.enabled && SubCamera.gameObject.activeInHierarchy;

        /// <summary>
        /// Returns true if this ProPixelizerCamera is currently operating as a main camera paired with an active subcamera.
        /// </summary>
        public bool OperatingAsMainCamera => enabled && HasActiveSubCamera;

        [Tooltip("The fixed resolution (height) of the low-resolution target.")]
        public int FixedTargetResolution = 320;

        //https://github.com/ProPixelizer/ProPixelizer/issues/181
        [Tooltip("Clamps the orthographic size of the camera to maintain fixed world-space pixel size when the low-resolution target " +
            "is at maximum resolution. When disabled, the camera ortho size may increase beyond this limit, but world space pixel size will not be constrained.")]
        public bool ClampCameraOrthoSize = true;

        /// <summary>
        /// Camera ortho size before clamping is applied.
        /// </summary>
        private float _OriginalCameraOrthoSize;

        /// <summary>
        /// Stores whether the last render was able to satisfy the constraint for world-space pixel size for the desired orthographic view size.
        /// 
        /// (Debug Info for user and editor purposes)
        /// </summary>
        public WorldSpacePixelSizeConstraintStatus WSPixelSizeConstraintStatus { get; private set; }

        /// <summary>
        /// Mode to use this camera snap SRP in.
        /// </summary>
        public PixelSizeMode Mode;

        /// <summary>
        /// World-space unit size of a pixel in the low-res render target.
        /// 
        /// (This is a user-controlled parameter. If you want the actual world space pixel size, see LowResPixelSizeWS)
        /// </summary>
        //[Min(0.001f)]
        [Range(0.01f, 1f)]
        [Tooltip("World-space unit size of a pixel (for an object with pixelSize=1).")]
        public float PixelSize = 0.032f;

        /// <summary>
        /// World-space unit size of a pixel in the low-res render target.
        /// </summary>
        public float LowResPixelSizeWS { get; private set; }

        public int LowResTargetWidth { get; private set; }
        public int LowResTargetHeight { get; private set; }
        public float OrthoLowResWidth { get; private set; }
        public float OrthoLowResHeight { get; private set; }
        /// <summary>
        /// Coordinates denoting the centre of the screen camera's view on the low-res target, UV-space.
        /// </summary>
        public Vector2 LowResCameraDeltaUV { get; private set; }
        /// <summary>
        /// Shift of the low-resolution (phantom) camera relative to the high-screen screen camera, world-space.
        /// </summary>
        public Vector3 LowResCameraDeltaWS { get; private set; }
        public float LowResPerspectiveVerticalFOV { get; private set; }

        public bool overrideUsePixelExpansion;

        [Tooltip("Whether to perform ProPixelizer's 'Pixel Expansion' method of pixelation. This is required for per-object pixelisation and for moving pixelated objects at apparent sub-pixel resolutions. See ProPixelizer Render Feature.")]
        public bool UsePixelExpansion = true;
        [Tooltip("Controls which objects in the scene should be pixelated.")]
        public PixelizationFilter PixelizationFilter;
        [Tooltip("Enable use a pixel art upscaling filter that reduces aliasing.")]
        public bool UsePixelArtUpscalingFilter = true;

        [Tooltip("Scale of the low-resolution target.")]
        [Range(0.01f, 1.0f)]
        public float TargetScale = 1f;

        [Tooltip("Look-up table to use for full-screen color grading (see ProPixelizer's Palette tools to create your own).")]
        public Texture2D FullscreenColorGradingLUT;

        [Tooltip("Should ProPixelizer be used for this camera? You can use this to disable the ProPixelizer rendering passes on specific cameras.")]
        public bool EnableProPixelizer = true;

        public void UpdateCamera()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
                if (_camera == null)
                    return;
            }

            PixelSize = Mathf.Abs(PixelSize);
            _OriginalCameraOrthoSize = _camera.orthographicSize;

            ConfigureLowResTargetSize();

            OrthoLowResWidth = LowResTargetWidth * LowResPixelSizeWS;
            OrthoLowResHeight = LowResTargetHeight * LowResPixelSizeWS;

            if (!_camera.orthographic) {
                var fovDegreePerPixel = _camera.fieldOfView / (LowResTargetHeight - 2 * ProPixelizerUtils.LOW_RESOLUTION_TARGET_MARGIN);
                LowResPerspectiveVerticalFOV = fovDegreePerPixel * LowResTargetHeight;
            }
        }

        /// <summary>
        /// For orthographic cameras, calculates the size of a pixel in world-space units.
        /// 
        /// (Perspective cameras do not have constant pixel size and so the number is meaningless).
        /// </summary>
        float ScreenWorldSpacePixelSize => 2.0f * _camera.orthographicSize / _camera.scaledPixelHeight;

        /// <summary>
        /// Gets the world space pixel size that produces the specified Screen to low-res target 
        /// pixel ratio when operating in WorldSpacePixelSize mode.
        /// 
        /// Assumes the camera's orthographicSize and resolution are fixed.
        /// </summary>
        /// <param name="ratio">The number of screen pixels for every low-res target pixel. 
        /// For example, a ratio of two means that there is one low-res pixel for every two screen pixels.</param>
        public float GetWorldSpacePixelSizeForScreenToTargetRatio(float ratio)
        {
            if (_camera == null)
                throw new NullReferenceException("_camera cannot be null");
            return ScreenWorldSpacePixelSize * ratio;
        }

        /// <summary>
        /// Gets the camera orthographicSize that produces the specified Screen to low-res target 
        /// pixel ratio when operating in WorldSpacePixelSize mode.
        /// 
        /// Assumes the low-res target's WorldSpacePixelSize and the screen resolution are fixed.
        /// </summary>
        /// <param name="ratio">The number of screen pixels for every low-res target pixel. 
        /// For example, a ratio of two means that there is one low-res pixel for every two screen pixels.</param>
        public float GetOrthoSizeForScreenToTargetRatio(float ratio)
        {
            if (_camera == null)
                throw new NullReferenceException("_camera cannot be null");
            // Find out the camera ortho size that would give a screen world-space pixel size equal to the low-res pixel size / ratio.
            float screenPixelSize = LowResPixelSizeWS / ratio;
            return _camera.scaledPixelHeight * screenPixelSize / 2f;
        }

        /// <summary>
        /// Calculates the required low-res target size for the current PixelSizeMode.
        /// </summary>
        public void CalculateLowResTargetSize(out int width, out int height)
        {
            switch (Mode)
            {
                default:
                case PixelSizeMode.FixedDownscalingRatio:
                    ProPixelizerUtils.CalculateLowResTargetSize(_camera.scaledPixelWidth, _camera.scaledPixelHeight, TargetScale, out width, out height);
                    break;
                case PixelSizeMode.WorldSpacePixelSize:
                    // For performance reasons, restrict the size of the low-res target to no greater than the screen size.
                    float screenPixelSizeWS = 2.0f * _camera.orthographicSize / _camera.scaledPixelHeight;
                    float scale = screenPixelSizeWS / PixelSize;
                    scale = Mathf.Clamp01(scale);
                    ProPixelizerUtils.CalculateLowResTargetSize(_camera.scaledPixelWidth, _camera.scaledPixelHeight, scale, out width, out height);
                    break;
                case PixelSizeMode.FixedTargetResolution:
                    ProPixelizerUtils.CalculateLowResTargetSize(_camera.scaledPixelWidth, _camera.scaledPixelHeight, (float)FixedTargetResolution / _camera.scaledPixelHeight, out width, out height);
                    break;
            }
        }

        public void ConfigureLowResTargetSize()
        {
            int width, height;
            CalculateLowResTargetSize(out width, out height);
            LowResTargetWidth = width;
            LowResTargetHeight = height;
            
            WSPixelSizeConstraintStatus = WorldSpacePixelSizeConstraintStatus.Satisfied;

            switch (Mode)
            {
                default:
                case PixelSizeMode.FixedDownscalingRatio:
                case PixelSizeMode.FixedTargetResolution:
                    LowResPixelSizeWS = 2f * _camera.orthographicSize / (LowResTargetHeight - 2 * ProPixelizerUtils.LOW_RESOLUTION_TARGET_MARGIN);
                    break;
                case PixelSizeMode.WorldSpacePixelSize:
                    LowResPixelSizeWS = PixelSize;

                    // We cap the orthographic size to make sure the camera cannot view outside the area spanned by the pixelated target.
                    // This limit excludes the margin size because we do not want to be able to see the margin where outlines can be incorrect.
                    float cameraOrthoSizeLimit = (LowResTargetHeight - 2 * ProPixelizerUtils.LOW_RESOLUTION_TARGET_MARGIN) / 2f * LowResPixelSizeWS;
                    if (cameraOrthoSizeLimit < _camera.orthographicSize)
                    {
                        // Either clamp ortho size, or adjust the pixel size.
                        if (ClampCameraOrthoSize)
                        {
                            _camera.orthographicSize = Mathf.Max(Mathf.Min(_camera.orthographicSize, cameraOrthoSizeLimit), 0.0001f);
                            WSPixelSizeConstraintStatus = WorldSpacePixelSizeConstraintStatus.CannotMaintainOrthoSize;
                        }
                        else
                        {
                            LowResPixelSizeWS = 2f * _camera.orthographicSize / LowResTargetHeight;
                            WSPixelSizeConstraintStatus = WorldSpacePixelSizeConstraintStatus.CannotMaintainWSPixelSize;
                        }
                    }
                    break;
            }
        }

        public enum WorldSpacePixelSizeConstraintStatus
        {
            Satisfied,
            CannotMaintainWSPixelSize,
            CannotMaintainOrthoSize
        }

        public void OnEnable()
        {
            _camera = GetComponent<Camera>();
            WSPixelSizeConstraintStatus = WorldSpacePixelSizeConstraintStatus.Satisfied;
            Subscribe();
        }

        public void OnDisable() => Unsubscribe();

        public void Subscribe()
        {
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            RenderPipelineManager.beginContextRendering += BeginContextRendering;
            RenderPipelineManager.endContextRendering += EndContextRendering;
        }

        public void Unsubscribe()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RenderPipelineManager.beginContextRendering -= BeginContextRendering;
            RenderPipelineManager.endContextRendering -= EndContextRendering;
        }

        public void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;
            Snap();
            ConfigureCameraAtFrameStart();
        }

        public void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;
            Release();
            RestoreCameraConfigurationAtFrameEnd();
        }

        public void BeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            UpdateCamera();
        }

        public void EndContextRendering(ScriptableRenderContext context, List<Camera> cameras) {
            if (_camera != null)
                _camera.orthographicSize = _OriginalCameraOrthoSize;
        }

        public Vector3 PreSnapCameraPosition { get; private set; }
        public Quaternion PreSnapCameraRotation { get; private set; }

        /// <summary>
        /// Returns true if we should perform snapping.
        /// </summary>
        public bool ShouldPerformSnapping => _camera != null && _camera.orthographic;

        public void Snap()
        {
            if (!Application.isPlaying)
                return;

            if (_camera == null)
            {
                // This happens if the camera object gets deleted - but rendering pipeline asset still exists.
                Unsubscribe();
                return;
            }

            if (!ShouldPerformSnapping)
                return;

            var pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipelineAsset == null)
                throw new System.Exception("ProPixelizer requires the Universal Render Pipeline, but no UniversalRenderPipelineAsset is currently in use. Check you have assigned a RenderPipelineAsset in QualitySettings and/or GraphicsSettings.");
            float renderScale = pipelineAsset.renderScale;

            PreSnapCameraPosition = transform.position;
            PreSnapCameraRotation = transform.rotation;

            //Find all objects required to snap and release.
            if (_SnapManager == null || !_SnapManager.isActiveAndEnabled)
                _SnapManager = SceneObjectSnapManager.GetSceneSingleton();

            foreach (ObjectRenderSnapable snapable in _SnapManager)
                snapable.SaveTransform();

            foreach (ObjectRenderSnapable snapable in _SnapManager)
                if (snapable.enabled)
                    snapable.SnapAngles(this);

            // We want to calculate low-resolution transforms based on the unsnapped camera postion.
            // Although ProPixelizerCamera doesn't require ObjectRenderSnapable on the same transform,
            // there's nothing to prevent a user from adding it, or nesting the camera in a heirachy.
            // Thus we assert here that the camera position and rotation must be the _unsnapped_ versions
            // for the calculations.
            transform.rotation = PreSnapCameraRotation;
            transform.position = PreSnapCameraPosition;

            _camera.ResetWorldToCameraMatrix();
            Matrix4x4 originalCamToWorld = _camera.cameraToWorldMatrix;
            Matrix4x4 originalWorldToCam = _camera.worldToCameraMatrix;

            // Shift the view matrix of the low-resolution pass so that the world-space origin will
            // line up with a pixel of the low-resolution target. Doing this prevents pixel creep
            // for stationary objects.
            // 
            // originPosCS: Position of world origin in camera space.
            // roundedOriginPosCS: position of nearest pixel to the origin, in camera space.
            // LowResCameraDeltaWS: The delta to go from world origin to the nearest low-res pixel, in world space.
            float bias = 0.5f;
            Vector3 originPosCS = originalWorldToCam.MultiplyPoint(new Vector3());
            var roundedOriginPosCS = LowResPixelSizeWS * new Vector3(
                        Mathf.RoundToInt(originPosCS.x / LowResPixelSizeWS) + bias,
                        Mathf.RoundToInt(originPosCS.y / LowResPixelSizeWS) + bias,
                        Mathf.RoundToInt(originPosCS.z / LowResPixelSizeWS) + bias
                );
            LowResCameraDeltaWS = originalCamToWorld.MultiplyPoint(roundedOriginPosCS);

            // Adjust the camera view matrix and inverse to render the scene so that the world origin aligns to
            // the pixel grid of the camera.
            // 
            // Maybe we should be using the low-res matrices in place of the camera ones?
            // -- We already are; worldToCameraMatrix is the same as view matrix, and all
            //    that happens with the view matrix is that it is post-multiplied by
            //    the LowResCameraDeltaWS:
            //    `viewMatrix = viewMatrix * Matrix4x4.Translate(LowResCameraDeltaWS);`
            Matrix4x4 lowResCamToWorld = Matrix4x4.Translate(-LowResCameraDeltaWS) * originalCamToWorld;
            Matrix4x4 lowResWorldToCam = originalWorldToCam * Matrix4x4.Translate(LowResCameraDeltaWS);
            //transform.position -= LowResCameraDeltaWS;

            // delta sized in world-space units, in the space of the camera.
            var LowResCameraDeltaCS = (roundedOriginPosCS - originPosCS);

            LowResCameraDeltaUV = new Vector2(
                LowResCameraDeltaCS.x / OrthoLowResWidth,
                LowResCameraDeltaCS.y / OrthoLowResHeight
                );
            // compare this to LowResTargetWidth and Height to determine the centre of the origin-snapped low-res target relative
            // to the unsnapped screen target camera. This coordinate shift should be added to 0.5, 0.5 and used as centre.

            // Note that low res target render matrix should also use the shifted position in WS units.

            // Snap all objects to integer pixels in camera space.
            foreach (ObjectRenderSnapable snapable in _SnapManager)
            {
                if (!snapable.enabled)
                    continue;
                Vector3 snappedPosition;
                if (snapable.AlignPixelGrid)
                {
                    // Aligned objects should be separated by an integer number of macropixels as close as possible to the unsnapped delta.
                    var unsnappedReferencePixelPositionCamSpace = originalWorldToCam.MultiplyPoint(snapable.PixelGridReferencePosition) / LowResPixelSizeWS;
                    var unsnappedPixelPositionCamSpace = originalWorldToCam.MultiplyPoint(snapable.WorldPositionPreSnap) / LowResPixelSizeWS;
                    var unsnappedPixelDeltaCamSpace = unsnappedPixelPositionCamSpace - unsnappedReferencePixelPositionCamSpace;

                    // The reference position will be snapped as if it were a non-aligned snapable.
                    var snappedReferencePixelPositionCamSpace = new Vector3(
                        Mathf.RoundToInt(unsnappedReferencePixelPositionCamSpace.x) + OFFSET_BIAS,
                        Mathf.RoundToInt(unsnappedReferencePixelPositionCamSpace.y) + OFFSET_BIAS,
                        Mathf.RoundToInt(unsnappedReferencePixelPositionCamSpace.z) + OFFSET_BIAS
                    );

                    // We snap the aligned object an integer number of macropixels away from it:
                    var pixelExpansionSize = snapable.AlignmentPixelSize;
                    var roundedPixelDeltaCamSpace = new Vector3(
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.x / pixelExpansionSize),
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.y / pixelExpansionSize),
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.z / pixelExpansionSize)
                    ) * pixelExpansionSize;

                    snappedPosition = lowResCamToWorld.MultiplyPoint((snappedReferencePixelPositionCamSpace + roundedPixelDeltaCamSpace) * LowResPixelSizeWS);
                }
                else
                {
                    // get unsnapped pixel delta between snapable and camera, in camera space.
                    var unsnappedPixelDeltaCamSpace = originalWorldToCam.MultiplyPoint(snapable.WorldPositionPreSnap) / LowResPixelSizeWS;
                    // round delta to integer number of pixels
                    var snappedPixelPositionCamSpace = new Vector3(
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.x) + OFFSET_BIAS,
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.y) + OFFSET_BIAS,
                        Mathf.RoundToInt(unsnappedPixelDeltaCamSpace.z) + OFFSET_BIAS
                        );
                    // find corresponding position from low-res target projection.
                    snappedPosition = lowResCamToWorld.MultiplyPoint(snappedPixelPositionCamSpace * LowResPixelSizeWS);
                }

                if (snapable.SnapPosition && LowResPixelSizeWS > float.Epsilon)
                    snapable.transform.position = snappedPosition;
            }

            // Restore camera position to unsnapped location.
            transform.position = PreSnapCameraPosition;
            transform.rotation = PreSnapCameraRotation;
            // Problem: Restoring will affect snapped children that we don't want to unsnap?
        }

        const float OFFSET_BIAS = 0.25f;

        public void Release()
        {
            if (!Application.isPlaying)
                return;
            if (!ShouldPerformSnapping)
                return;

            // Note: the `release' loop is run in reverse.
            // This prevents shaking and movement from occuring when
            // parent and child transforms are both Snapable.
            //  e.g. For a heirachy A>B:
            //   * Snap A, then B.
            //   * Unsnap B, then A.
            // Doing Unsnap A, then unsnap B will produce jerking.
            var reversed = _SnapManager.Reverse();
            while (reversed.MoveNext())
                reversed.Current.RestoreTransform();
            // Also reset camera explicitly. Otherwise, when camera is child of a rotation or position snapped object,
            // The 'unsnapping' of the camera before render will leave the camera unsnapped when the object transform
            // is snapped, and restore transform will create drift.
            transform.position = PreSnapCameraPosition;
            transform.rotation = PreSnapCameraRotation;
        }

        public void ModifySettings(ref ProPixelizerSettings settings)
        {
            // camera specific overrides of settings.
            if (FullscreenColorGradingLUT != null)
                settings.FullscreenLUT = FullscreenColorGradingLUT;
            settings.Enabled = EnableProPixelizer;
            settings.Filter = PixelizationFilter;
            settings.UsePixelArtUpscalingFilter = UsePixelArtUpscalingFilter;
            if (overrideUsePixelExpansion)
                settings.UsePixelExpansion = UsePixelExpansion;
            if (OperatingAsMainCamera)
                settings.RenderingPathway = ProPixelizerRenderingPathway.MainCamera;
        }

        int _cachedCullingMaskSetting;
        bool _cachedRenderShadowsSetting;
        bool configuredCamera;
        CameraClearFlags _cachedCameraClearFlags;

        public void ConfigureCameraAtFrameStart()
        {
            configuredCamera = false;
            var universal = _camera.GetUniversalAdditionalCameraData();
            if (universal == null) return;
            if (!EnableProPixelizer) return;
            configuredCamera = true;
            _cachedCullingMaskSetting = _camera.cullingMask;
            _cachedRenderShadowsSetting = universal.renderShadows;
            _cachedCameraClearFlags = _camera.clearFlags;
            if (OperatingAsMainCamera && PixelizationFilter == PixelizationFilter.FullScene)
            {
                // In this mode, main camera only exists to blit the texture back to screen. Turn off everything we don't need.
                // (People might still want to do high res post processing so leave that on).
                _camera.cullingMask = _cachedCullingMaskSetting & SubcameraExcludedLayers.value;
                universal.renderShadows = false;
                _camera.clearFlags = CameraClearFlags.Depth | CameraClearFlags.Color; // no skybox
            }
        }

        public void RestoreCameraConfigurationAtFrameEnd()
        {
            var universal = _camera.GetUniversalAdditionalCameraData();
            if (universal == null)
                return;
            if (!configuredCamera)
                return;
            _camera.cullingMask = _cachedCullingMaskSetting;
            _camera.clearFlags = _cachedCameraClearFlags;
            universal.renderShadows = _cachedRenderShadowsSetting;
        }

    }
}
