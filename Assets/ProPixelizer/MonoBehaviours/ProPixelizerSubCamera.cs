// Copyright Elliot Bentine, 2018-
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// ProPixelizer's subcamera is used for rendering a low-resolution view of the scene.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class ProPixelizerSubCamera : MonoBehaviour
    {
        Camera _camera;
        RenderTexture _CameraTarget;
        RTHandle _Color;
        RTHandle _Depth;
        ProPixelizerCamera _proPixelizerCamera;

        public ProPixelizerCamera MainCamera => _proPixelizerCamera;

        public RTHandle Color { get { return _Color; } }
        public RTHandle Depth { get { return _Depth; } }

        private void Initialise()
        {
            _camera = GetComponent<Camera>();
            Configure();
        }

        public void Update()
        {
            // Automatically disable subcamera when not required.
            if (MainCamera == null || !MainCamera.isActiveAndEnabled || !MainCamera.OperatingAsMainCamera)
            {
                gameObject.SetActive(false);
            }
        }

        public void Configure()
        {
            _proPixelizerCamera = transform.parent.GetComponent<ProPixelizerCamera>();
            if (_proPixelizerCamera == null)
            {
                Debug.LogError("A ProPixelizerSubCamera without a main camera has been detected. The ProPixelizerSubCamera MonoBehavior should be attached to a camera, which is a child of the main camera with a ProPixelizerCamera MonoBehavior.");
                return;
            }
            _proPixelizerCamera.SubCamera = this;
        }

        public void MirrorMainCameraParameters()
        {
            var parentCamera = transform.parent.GetComponent<Camera>();
            if (parentCamera != null)
            {
                _camera.depth = parentCamera.depth - 1;
                _camera.orthographic = parentCamera.orthographic;
                _camera.nearClipPlane = parentCamera.nearClipPlane;
                _camera.farClipPlane = parentCamera.farClipPlane;
                _camera.cullingMask = parentCamera.cullingMask & (int.MaxValue ^ _proPixelizerCamera.SubcameraExcludedLayers.value);

                var parentData = parentCamera.GetUniversalAdditionalCameraData();
                var data = _camera.GetUniversalAdditionalCameraData();
                data.renderShadows = parentData.renderShadows;
                data.dithering = parentData.dithering;
                data.requiresDepthTexture = parentData.requiresDepthTexture;
            }
        }

        public void OnEnable()
        {
            Initialise();
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            RenderPipelineManager.beginContextRendering += BeginContextRendering;
            RenderPipelineManager.endContextRendering += EndContextRendering;
        }

        public void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RenderPipelineManager.beginContextRendering -= BeginContextRendering;
            RenderPipelineManager.endContextRendering -= EndContextRendering;

            CleanupSubcameraResources();
            _Color?.Release();
            _Depth?.Release();
            _Color = null;
            _Depth = null;
        }

        public void ModifySettings(ref ProPixelizerSettings settings)
        {
            if (MainCamera == null) return;
            MainCamera.ModifySettings(ref settings);
            settings.RenderingPathway = ProPixelizerRenderingPathway.SubCamera;
        }

        private float _cachedRenderScale;

        public RenderTextureDescriptor GetColorTextureDescriptor(Camera camera)
        {
            var format = camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            var colorDescriptor = new RenderTextureDescriptor(
                _proPixelizerCamera.LowResTargetWidth, _proPixelizerCamera.LowResTargetHeight, format, 0);
            colorDescriptor.depthBufferBits = 32;
            return colorDescriptor;
        }

        public void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;

            // perform snapping of associated ProPixelizer camera
            _proPixelizerCamera.Snap();

            // For an orthographic camera, adjust parameters to match the low-resolution target settings.
            if (camera.orthographic)
            {
                camera.orthographicSize = _proPixelizerCamera.OrthoLowResHeight / 2f;
                transform.position = transform.parent.position - _proPixelizerCamera.LowResCameraDeltaWS;
                _camera.ResetWorldToCameraMatrix();
            }

            var cameraTargetDescriptor = GetColorTextureDescriptor(camera);

            cameraTargetDescriptor.useMipMap = false;
            cameraTargetDescriptor.autoGenerateMips = false;
            TryReleaseCameraTarget();
            CreateAndSetCameraTarget(cameraTargetDescriptor);

            var colorDescriptor = cameraTargetDescriptor;
            colorDescriptor.depthBufferBits = 0;

            var depthDescriptor = colorDescriptor;
            depthDescriptor.colorFormat = RenderTextureFormat.Depth;
            depthDescriptor.depthBufferBits = 32;

#if UNITY_6000_0_OR_NEWER
            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _Color,
                in colorDescriptor,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp,
                name: ProPixelizerTargets.PROPIXELIZER_SUBCAMERA_COLOR
                );
            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _Depth,
                in depthDescriptor,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp,
                name: ProPixelizerTargets.PROPIXELIZER_SUBCAMERA_DEPTH
                );
#else
            RenderingUtils.ReAllocateIfNeeded(
                ref _Color, in colorDescriptor,
                name: ProPixelizerTargets.PROPIXELIZER_SUBCAMERA_COLOR,
                filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp
                );
            RenderingUtils.ReAllocateIfNeeded(
                ref _Depth, in depthDescriptor,
                name: ProPixelizerTargets.PROPIXELIZER_SUBCAMERA_DEPTH,
                filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp
                );
#endif
            // Renderscale must be 1 for subcameras - we manually set the texture size according to target world space pixel size.
            _cachedRenderScale = ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale;
            ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale = 1f;
        }
        
        public void ConfigureSubcameraAtFrameStart()
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorTexture = false;
            data.requiresDepthTexture = false;

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            MirrorMainCameraParameters();
        }

        public void BeginContextRendering(ScriptableRenderContext ctx, List<Camera> cameras) => ConfigureSubcameraAtFrameStart();
        public void EndContextRendering(ScriptableRenderContext ctx, List<Camera> cameras) => CleanupSubcameraResources();

        public void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera)
                return;

            // unsnap associated ProPixelizer camera
            _proPixelizerCamera.Release();
            ((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).renderScale = _cachedRenderScale;
        }

        public void CleanupSubcameraResources()
        {
            TryReleaseCameraTarget();
        }

        private void CreateAndSetCameraTarget(RenderTextureDescriptor cameraTargetDescriptor)
        {
            _CameraTarget = RenderTexture.GetTemporary(cameraTargetDescriptor);
            _CameraTarget.name = ProPixelizerTargets.PROPIXELIZER_SUBCAMERA_TARGET;
            _CameraTarget.filterMode = FilterMode.Point;
            _CameraTarget.wrapMode = TextureWrapMode.Clamp;
            _camera.targetTexture = _CameraTarget;
        }

        private void TryReleaseCameraTarget()
        {
            if (_CameraTarget == null)
                return;

            if (_camera != null && _camera.targetTexture == _CameraTarget)
                _camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(_CameraTarget);
            _CameraTarget = null;
        }
    }
}
