// Copyright Elliot Bentine, 2018-
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Reconfigures draw target and projection matrices when for rendering to the low-res target.
    /// </summary>
    public class SetLowResProjectionMatricesPass : ProPixelizerPass
    {
        public SetLowResProjectionMatricesPass(
            RenderPassEvent passEvent,
            ProPixelizerLowResolutionConfigurationPass configPass
            )
        {
            renderPassEvent = passEvent;
            ConfigurationPass = configPass;
        }

        ProPixelizerLowResolutionConfigurationPass ConfigurationPass;


#if !PRERG_API_REMOVED
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Preview) // preview camera badly bugged, doesnt call all URP frame methods
                return;

            CommandBuffer buffer = CommandBufferPool.Get(nameof(SetLowResProjectionMatricesPass));
            if (Settings.Filter == PixelizationFilter.FullScene)
            {
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
                    out var viewMatrix,
                    out var projectionMatrix
                );
#if UNITY_2023_3_OR_NEWER
                buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
#else
                buffer.SetViewMatrix(viewMatrix);
                buffer.SetProjectionMatrix(projectionMatrix);
#endif
                buffer.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW, viewMatrix);
                buffer.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION, projectionMatrix);
                buffer.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_VIEW_INVERSE, viewMatrix.inverse);
                buffer.SetGlobalMatrix(ProPixelizerMatrices.LOW_RES_PROJECTION_INVERSE, projectionMatrix.inverse);
            }

            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }

#endif
    }
}

#pragma warning restore 0672