// Copyright Elliot Bentine, 2018-
using UnityEngine;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#endif

namespace ProPixelizer
{
#if UNITY_2023_3_OR_NEWER
    public class ProPixelizerTargetConfigurationContextItem : ContextItem
    {
        public ProPixelizerTargetConfiguration Config;
        public override void Reset()
        {
            // all properties written each frame, no need to reset.
        }
    }
#endif

    /// <summary>
    /// Description of the final camera target. 
    /// </summary>
    public struct CameraTargetDescriptor
    {
        public int width;
        public int height;
    }

    /// <summary>
    /// Represents a configuration of a low resolution target.
    /// </summary>
    public struct ProPixelizerTargetConfiguration
    {
        /// <summary>
        /// Width of the ProPixelizer low resolution target
        /// </summary>
        public int Width;

        /// <summary>
        /// Height of the ProPixelizer low resolution target
        /// </summary>
        public int Height;

        /// <summary>
        /// Full span of the orthographic projection's width, in world-space units.
        /// </summary>
        public float OrthoLowResWidth;

        /// <summary>
        /// Full span of the orthographic projection's height, in world-space units.
        /// </summary>
        public float OrthoLowResHeight;

        /// <summary>
        /// Full span of the unpixelated camera's orthographic projection's width, in world-space units.
        /// </summary>
        public float CameraOrthoWidth;

        /// <summary>
        /// Full span of the unpixelated camera's orthographic projection's height, in world-space units.
        /// </summary>
        public float CameraOrthoHeight;

        /// <summary>
        /// Offset of the low-resolution camera relative to the high-res screen camera, in units of low-res uv coordinates.
        /// </summary>
        public Vector2 LowResCameraDeltaUV;

        /// <summary>
        /// Offset of the low-resolution camera relative to the high-res screen camera, in world-space units.
        /// </summary>
        public Vector3 LowResCameraDeltaWS;

        /// <summary>
        /// Vertical field-of-view of the low resolution target.
        /// </summary>
        public float LowResPerspectiveVerticalFOV;

        public float LowResAspect => (float)Width / Height;

        public static ProPixelizerTargetConfiguration From(
            CameraTargetDescriptor descriptor,
            Camera camera,
            ref ProPixelizerSettings settings
            )
        {
            ProPixelizerCamera proPCamera = camera?.GetComponent<ProPixelizerCamera>();

            // width and height can be zero in editor mode, when the camera update has not yet run.
            if (proPCamera != null && proPCamera.LowResTargetHeight > 0 && proPCamera.LowResTargetWidth > 0)
            {
                int width = settings.RecompositionMode == RecompositionMode.None ? descriptor.width : proPCamera.LowResTargetWidth;
                int height = settings.RecompositionMode == RecompositionMode.None ? descriptor.height : proPCamera.LowResTargetHeight;
                return new ProPixelizerTargetConfiguration
                {
                    Width = width,
                    Height = height,
                    OrthoLowResWidth = proPCamera.OrthoLowResWidth,
                    OrthoLowResHeight = proPCamera.OrthoLowResHeight,
                    LowResCameraDeltaUV = proPCamera.LowResCameraDeltaUV,
                    LowResCameraDeltaWS = proPCamera.LowResCameraDeltaWS,
                    LowResPerspectiveVerticalFOV = proPCamera.LowResPerspectiveVerticalFOV,
                    CameraOrthoHeight = 2.0f * camera.orthographicSize,
                    CameraOrthoWidth = 2.0f * camera.orthographicSize * descriptor.width / descriptor.height
                };
            }
            else
            {
                ProPixelizerUtils.CalculateLowResTargetSize(descriptor.width, descriptor.height, settings.DefaultLowResScale, out int width, out int height);

                int margin = ProPixelizerUtils.LOW_RESOLUTION_TARGET_MARGIN;
                if (camera.cameraType == CameraType.Preview)
                {
                    margin = 0;
                    // force same resolution for propixelizer targets.
                    width = descriptor.width;
                    height = descriptor.height;
                }

                if (settings.RecompositionMode == RecompositionMode.None)
                {
                    margin = 0;
                    width = descriptor.width;
                    height = descriptor.height;
                }

                var config = new ProPixelizerTargetConfiguration();
                config.Width = width;
                config.Height = height;

                if (camera != null)
                {
                    if (camera.orthographic)
                    {
                        config.OrthoLowResHeight = 2.0f * camera.orthographicSize * height / (height - 2 * margin);
                        config.OrthoLowResWidth = config.OrthoLowResHeight / height * width;
                    }
                    else
                    {
                        var fovDegreePerPixel = camera.fieldOfView / (height - 2 * margin);
                        config.LowResPerspectiveVerticalFOV = fovDegreePerPixel * height;
                    }
                }
                config.CameraOrthoHeight = 2.0f * camera.orthographicSize;
                config.CameraOrthoWidth = 2.0f * camera.orthographicSize * descriptor.width / descriptor.height;
                return config;
            }
        }
    }
}