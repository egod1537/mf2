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
    /// This pass copies the depth target from a Subcamera into a longer-lived RTHandle for use during
	/// recomposition for the main camera.
	/// 
	///  Used by both RG and non-RG pathways.
    /// </summary>
    public class ProPixelizerExportSubcameraDepthPass : ProPixelizerPass
	{
		public ProPixelizerExportSubcameraDepthPass(ShaderResources shaders)
		{
			renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
			Materials = new MaterialLibrary(shaders);
		}

        private MaterialLibrary Materials;

		#region non-RG

		private RTHandle _SubcameraDepthOutput;

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

			var subcamera = renderingData.cameraData.camera.GetComponent<ProPixelizerSubCamera>();
			_SubcameraDepthOutput = subcamera.Depth;
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
            if (_SubcameraDepthOutput == null)
                return;

            CommandBuffer buffer = CommandBufferPool.Get(nameof(ProPixelizerExportSubcameraDepthPass));
			bool isOverlay = renderingData.cameraData.camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Overlay;

			// Start copying ---------------

			// Copy depth into Color target so that transparents work in scene view tab
            Blitter.BlitCameraTexture(buffer, DepthTarget(ref renderingData), _SubcameraDepthOutput, Materials.CopyDepth, 0);

			// ...and restore transformations:
			// ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);
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
			public TextureHandle CameraDepth;
            public TextureHandle SubcameraDepthOutput;
			public Material copyDepth;
        }

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var subcamera = cameraData.camera.GetComponent<ProPixelizerSubCamera>();
			if (subcamera == null)
				return;

			var resources = frameData.Get<UniversalResourceData>();
			var subcameraTargets = frameData.GetOrCreate<ProPixelizerSubCameraTargets>();
			subcameraTargets.Depth = renderGraph.ImportTexture(subcamera.Depth,
				new ImportResourceParams
					{ 
						discardOnLastUse = false,
						clearOnFirstUse = true 
				}
			);

			using (var builder = renderGraph.AddUnsafePass<PassData>(nameof(ProPixelizerExportSubcameraDepthPass), out var passData))
			{
				passData.SubcameraDepthOutput = subcameraTargets.Depth;
				passData.CameraDepth = GetSceneDepthTexture(resources);
				passData.copyDepth = Materials.CopyDepth;

				builder.AllowPassCulling(false);
				builder.UseTexture(passData.SubcameraDepthOutput, AccessFlags.Write);
                builder.UseTexture(passData.CameraDepth, AccessFlags.Read);
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
            Blitter.BlitCameraTexture(command, data.CameraDepth, data.SubcameraDepthOutput, data.copyDepth, 0);
        }

#endif
#endregion

		/// <summary>
		/// Materials used by the PixelizationPass
		/// </summary>
		public sealed class MaterialLibrary
		{
			private ShaderResources Resources;
			private Material _CopyDepth;
			public Material CopyDepth
			{
				get
				{
					if (_CopyDepth == null)
						_CopyDepth = new Material(Resources.CopyDepth);
					return _CopyDepth;
				}
			}
			public MaterialLibrary(ShaderResources resources)
			{
				Resources = resources;
			}
		}
	}
}