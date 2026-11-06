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
    /// This pass copies the color target from a Subcamera into a longer-lived RTHandle for use during
	/// recomposition for the main camera.
	/// 
	///  Used by both RG and non-RG pathways.
    /// </summary>
    public class ProPixelizerExportSubcameraColorPass : ProPixelizerPass
	{
		public ProPixelizerExportSubcameraColorPass()
		{
			renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
		}

		#region non-RG
        #if !PRERG_API_REMOVED
		private RTHandle _SubcameraOutput;

		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
#if DISABLE_PROPIX_PREVIEW_WINDOW
			// Inspector window in 2022.2 is bugged with a null render target.
			// https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
			// https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
			if (renderingData.cameraData.cameraType == CameraType.Preview)
				return;
#endif

			var subcamera = renderingData.cameraData.camera.GetComponent<ProPixelizerSubCamera>();
            _SubcameraOutput = subcamera.Color;
        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
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
            if (_SubcameraOutput == null)
                return;

            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerExportSubcameraColorPass));
            Blitter.BlitCameraTexture(buffer, ColorTarget(ref renderingData), _SubcameraOutput);
			context.ExecuteCommandBuffer(buffer);
			CommandBufferPool.Release(buffer);
		}

        public void Dispose()
        {
        }
        #endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

		class PassData
		{
			public TextureHandle CameraColor;
            public TextureHandle SubcameraColorOutput;
        }

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var subcamera = cameraData.camera.GetComponent<ProPixelizerSubCamera>();
			if (subcamera == null)
				return;

			var resources = frameData.Get<UniversalResourceData>();
			var subcameraTargets = frameData.GetOrCreate<ProPixelizerSubCameraTargets>();
			subcameraTargets.Color = renderGraph.ImportTexture(subcamera.Color,
				new ImportResourceParams
					{ 
						discardOnLastUse = false,
						clearOnFirstUse = true 
					}
			);

			using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerExportSubcameraColorPass), out var passData))
			{
				passData.SubcameraColorOutput = subcameraTargets.Color;
				passData.CameraColor = CameraColorTexture(resources, cameraData);

				builder.AllowGlobalStateModification(true);
				builder.AllowPassCulling(false);
				builder.UseTexture(passData.SubcameraColorOutput, AccessFlags.Write);
                builder.UseTexture(passData.CameraColor, AccessFlags.Read);
				builder.SetRenderFunc(
					(PassData data, UnsafeGraphContext context) =>
					{
						ExecutePass(
							CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
							data
							);
					});
            }
        }

		static void ExecutePass(CommandBuffer command, PassData data)
		{
            Blitter.BlitCameraTexture(command, data.CameraColor, data.SubcameraColorOutput);
        }

#endif
        #endregion
	}
}