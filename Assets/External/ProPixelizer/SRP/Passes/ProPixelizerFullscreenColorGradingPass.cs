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
    /// Applies a color grading palette to the low-resolution color target before recomposition.
    /// </summary>
    public class ProPixelizerFullscreenColorGradingPass : ProPixelizerPass
	{
		public ProPixelizerFullscreenColorGradingPass(
            ShaderResources shaders,
            ProPixelizerMetadataPass metadata,
            ProPixelizerLowResolutionConfigurationPass configuration,
            ProPixelizerLowResolutionTargetPass target)
		{
			Materials = new MaterialLibrary(shaders);
            _Metadata = metadata;
            _Configuration = configuration;
            _Target = target;
        }

        
        private MaterialLibrary Materials;
        #region non-RG
        private RTHandle _Color;
        private ProPixelizerMetadataPass _Metadata;
        private ProPixelizerLowResolutionConfigurationPass _Configuration;
        private ProPixelizerLowResolutionTargetPass _Target;
        
        /// <summary>
        /// Returns true if full screen color grading is active.
        /// </summary>
        public bool Active => Settings.FullscreenLUT != null;

        /// <summary>
        /// Anchors the color grading dither pattern to the world origin for orthographic cameras.
        /// We calculate the offset here and then take modulo 4 pixels (the unit size of the ordered dither pattern)
        /// which prevents precision issues on platforms that default to half prec.
        /// 
        /// Perspective cameras use a zero offset (there's no benefit to shifting the pattern).
        /// </summary>
        private static Vector2 CalculateDitherPatternOffset(
            Matrix4x4 cameraView,
            bool orthographic,
            ProPixelizerTargetConfiguration config,
            ProPixelizerSettings settings)
        {
            if (!orthographic || config.Width <= 0 || config.Height <= 0 ||
                config.OrthoLowResWidth <= 0f || config.OrthoLowResHeight <= 0f)
                return Vector2.zero;

            var lowResView = settings.RenderingPathway == ProPixelizerRenderingPathway.SubCamera
                ? cameraView
                : cameraView * Matrix4x4.Translate(config.LowResCameraDeltaWS);
            var originVS = lowResView.MultiplyPoint(Vector3.zero);
            var originPixel = new Vector2(
                config.Width * (0.5f + originVS.x / config.OrthoLowResWidth),
                config.Height * (0.5f + originVS.y / config.OrthoLowResHeight));

            return new Vector2(
                Mathf.Repeat(-originPixel.x, 4f),
                Mathf.Repeat(-originPixel.y, 4f));
        }

#if !PRERG_API_REMOVED
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (Settings.TargetForPixelation == TargetForPixelation.ProPixelizerTarget && _Configuration.Data.Width > 0)
            {
                cameraTextureDescriptor.width = _Configuration.Data.Width;
                cameraTextureDescriptor.height = _Configuration.Data.Height;
            }
            cameraTextureDescriptor.useMipMap = false;
            cameraTextureDescriptor.depthBufferBits = 0;
#pragma warning disable 0618
            RenderingUtils.ReAllocateIfNeeded(ref _Color, cameraTextureDescriptor, name: ProPixelizerTargets.PROPIXELIZER_FULLSCREEN_COLOR_GRADING);
            ConfigureTarget(_Color);
            ConfigureClear(ClearFlag.None, Color.black);
#pragma warning restore 0618
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!Active) return;
            if (renderingData.cameraData.cameraType == CameraType.Preview)
                return;

            var colorTarget = ColorTargetForPixelization(ref renderingData, _Target);
            if (colorTarget == null) return;

            var colorGradingMaterial = Materials.FullScreenColorGradeMaterial;
            var ditherPatternOffset = CalculateDitherPatternOffset(
                renderingData.cameraData.GetViewMatrix(),
                renderingData.cameraData.camera.orthographic,
                _Configuration.Data,
                Settings);
            colorGradingMaterial.SetTexture(FullScreenColorGradingProperties.LUTPropertyName, Settings.FullscreenLUT);
            colorGradingMaterial.SetVector(
                FullScreenColorGradingProperties.COLOR_GRADING_DITHER_PATTERN_OFFSET,
                new Vector4(ditherPatternOffset.x, ditherPatternOffset.y, 0f, 0f)
            );

            CommandBuffer command = CommandBufferPool.Get(nameof(ProPixelizerFullscreenColorGradingPass));
            ExecutePass(
                command,
                colorGradingMaterial,
                colorTarget,
                _Color,
                _Metadata.MetadataObjectBuffer,
                Active
                );
            context.ExecuteCommandBuffer(command);
            CommandBufferPool.Release(command);
        }

        public void Dispose()
        {
            if (!Active) return;
            _Color?.Release();
        }
#endif
#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public Material colorGradingMaterial;
            public TextureHandle colorTarget;
            public TextureHandle temporaryColorTarget;
            public TextureHandle metadata;
            public bool active;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var lowResConfig = frameData.Get<ProPixelizerTargetConfigurationContextItem>().Config;
            var colorGradingMaterial = Materials.FullScreenColorGradeMaterial;
            var ditherPatternOffset = CalculateDitherPatternOffset(
                cameraData.GetViewMatrix(),
                cameraData.camera.orthographic,
                lowResConfig,
                Settings);
            colorGradingMaterial.SetTexture(FullScreenColorGradingProperties.LUTPropertyName, Settings.FullscreenLUT);
            colorGradingMaterial.SetVector(
                FullScreenColorGradingProperties.COLOR_GRADING_DITHER_PATTERN_OFFSET,
                new Vector4(ditherPatternOffset.x, ditherPatternOffset.y, 0f, 0f));

            var lowResTarget = frameData.Get<LowResTargetData>();
            var metadata = frameData.Get<MetadataTargetData>();

            var descriptor = lowResTarget.Color.GetDescriptor(renderGraph);
            descriptor.depthBufferBits = 0;
            descriptor.name = ProPixelizerTargets.PROPIXELIZER_FULLSCREEN_COLOR_GRADING;
            descriptor.clearBuffer = false;

            var tempColor = renderGraph.CreateTexture(descriptor);

            using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerFullscreenColorGradingPass), out var passData))
            {
                passData.colorGradingMaterial = colorGradingMaterial;
                passData.colorTarget = lowResTarget.Color;
                passData.temporaryColorTarget = tempColor;
                passData.metadata = metadata.Color;
                passData.active = Active;

                builder.UseTexture(passData.temporaryColorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.colorTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.metadata, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(
                    (PassData data, UnsafeGraphContext context) =>
                    {
                        ExecutePass(
                            CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                            data.colorGradingMaterial,
                            data.colorTarget,
                            data.temporaryColorTarget,
                            data.metadata,
                            data.active
                            );
                    }
                );
            }
        }

#endif
#endregion

        public static void ExecutePass(
            CommandBuffer command,
            Material colorGradingMaterial,
            RTHandle colorTarget,
            RTHandle temporaryColorTarget,
            RTHandle metadataObject,
            bool active
            )
        {
            if (!active)
                return;
            command.SetGlobalTexture("_ProPixelizer_Metadata", metadataObject);
            Blitter.BlitCameraTexture(command, colorTarget, temporaryColorTarget, colorGradingMaterial, 0);
            Blitter.BlitCameraTexture(command, temporaryColorTarget, colorTarget);
        }

		/// <summary>
		/// Materials used by the PixelizationPass
		/// </summary>
		public sealed class MaterialLibrary
		{
			private ShaderResources Resources;

			private Material _FullScreenColorGradeMaterial;
			public Material FullScreenColorGradeMaterial
            {
				get
				{
                    if (_FullScreenColorGradeMaterial == null)
                        _FullScreenColorGradeMaterial = new Material(Resources.FullScreenColorGrading);
					return _FullScreenColorGradeMaterial;
				}
			}

            public MaterialLibrary(ShaderResources resources)
			{
				Resources = resources;
			}
		}
	}
}
