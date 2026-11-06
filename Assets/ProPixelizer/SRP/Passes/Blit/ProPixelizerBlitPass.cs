// Copyright Elliot Bentine, 2018-
using ProPixelizer.RenderGraphResources;
using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
	/// <summary>
	/// Exposes a Pass in ProPixelizer as a source for the ProPixelizerBlitPass.
	/// </summary>
	public interface ExposesTargetForProPixelizerBlitPass
	{
#if !PRERG_API_REMOVED
        RTHandle GetTargetForBlitPassNonRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType);
#endif
#if UNITY_2023_3_OR_NEWER
        TextureHandle GetTargetForBlitPassRG(ProPixelizerBlitPass.ProPixelizerRTHandleType sourceType, ContextContainer context);
#endif
	}

    /// <summary>
    /// A utility pass for copying from one target to another.
    /// 
    /// It might seem a bit overkill given Unity has its own blit passes and API, but the Unity version changes across engine versions and I need compatibility across
    /// both 2022.3 (pre-RG) all the way to the new RenderGraph API. There were also bugs in historic CopyDepth passes. Hence this wrapper. It also owns the complexities
	/// required for blitting to camera depth target and attachment, which is fiddly.
    /// </summary>
    public sealed class ProPixelizerBlitPass : ProPixelizerPass
	{
		public enum ProPixelizerRTHandleType
		{
			Color,
			Depth,
		}

		public enum BlittableHandleType
		{
            Uninitialised,
			ProPixelizerColor,
            ProPixelizerDepth,
            CameraDepthTexture,
			CameraDepthAttachment,
            CameraColor
		}

		public static bool IsHandleTypeDepth(BlittableHandleType type)
		{
			switch (type)
			{
				case BlittableHandleType.ProPixelizerColor:
                case BlittableHandleType.CameraColor:
					return false;
				default:
					return true;
			}
		}

		public ProPixelizerBlitPass(
            ShaderResources shaders,
            RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques,
            BlittableHandleType source = BlittableHandleType.Uninitialised,
            BlittableHandleType target = BlittableHandleType.Uninitialised,
            ExposesTargetForProPixelizerBlitPass sourcePass = null,
            ExposesTargetForProPixelizerBlitPass targetPass = null
            )
		{
            this.renderPassEvent = renderPassEvent;
			Materials = new MaterialLibrary(shaders);
			Source = source;
			Target = target;
			SourcePass = sourcePass;
			TargetPass = targetPass;
		}

		public BlittableHandleType Source;
        public BlittableHandleType Target;
        public ExposesTargetForProPixelizerBlitPass SourcePass;
        public ExposesTargetForProPixelizerBlitPass TargetPass;
        MaterialLibrary Materials;

        readonly static string NAME_BASE = "ProPixelizerBlitPass";
        readonly static string NAME_BLIT_COLOR = "ProPixelizerBlitPass: Color";
        readonly static string NAME_BLIT_DEPTH = "ProPixelizerBlitPass: Depth";
        readonly static string NAME_BLIT_CAMERA_DEPTH_TARGET = "ProPixelizerBlitPass: CameraDepthTarget";
        readonly static string NAME_BLIT_CAMERA_DEPTH_ATTACHMENT = "ProPixelizerBlitPass: CameraDepthAttachment";
        readonly static string NAME_BLIT_CAMERA_COLOR = "ProPixelizerBlitPass: CameraColor";

        public string ProfilingName
        {
            get
            {
                switch (Target)
                {
                    case BlittableHandleType.ProPixelizerColor: return NAME_BLIT_COLOR;
                    case BlittableHandleType.ProPixelizerDepth: return NAME_BLIT_DEPTH;
                    case BlittableHandleType.CameraDepthAttachment: return NAME_BLIT_CAMERA_DEPTH_ATTACHMENT;
                    case BlittableHandleType.CameraDepthTexture: return NAME_BLIT_CAMERA_DEPTH_TARGET;
                    case BlittableHandleType.CameraColor: return NAME_BLIT_CAMERA_COLOR;
                    default: return NAME_BASE;
                }
            }
        }

        public ProPixelizerRTHandleType FromBlittableHandle(BlittableHandleType blittableHandle) => blittableHandle == BlittableHandleType.ProPixelizerColor ? ProPixelizerRTHandleType.Color : ProPixelizerRTHandleType.Depth;

        #region non-RG
#if !PRERG_API_REMOVED

        public RTHandle GetBlitTextureNonRG(ref RenderingData renderingData, bool isSource = true) {
            var textureType = isSource ? Source : Target;
            var pass = isSource ? SourcePass : TargetPass;
            switch (textureType)
            {
                case BlittableHandleType.ProPixelizerColor:
                case BlittableHandleType.ProPixelizerDepth:
                    return pass.GetTargetForBlitPassNonRG(FromBlittableHandle(textureType));
                case BlittableHandleType.CameraDepthTexture:
                    return NonRGTryGetCameraDepthTarget(ref renderingData);
                case BlittableHandleType.CameraDepthAttachment:
                    return DepthTarget(ref renderingData);
                case BlittableHandleType.CameraColor:
                    return ColorTarget(ref renderingData);
                default:
                    throw new NotImplementedException();
            }
        }

        
		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
#if DISABLE_PROPIX_PREVIEW_WINDOW
			// Inspector window in 2022.2 is bugged with a null render target.
			// https://forum.unity.com/threads/preview-camera-does-not-call-setuprenderpasses.1377363/
			// https://forum.unity.com/threads/nullreferenceexception-in-2022-2-1f1-urp-14-0-4-when-rendering-the-inspector-preview-window.1377240/
			if (renderingData.cameraData.cameraType == CameraType.Preview)
				return;
#endif
#pragma warning disable 0618
			var target = GetBlitTextureNonRG(ref renderingData, isSource: false);
			if (target != null)
				ConfigureTarget(target);
#pragma warning restore 0618
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
            var source = GetBlitTextureNonRG(ref renderingData, true);
            var target = GetBlitTextureNonRG(ref renderingData, false);
            if (source == null || target == null)
                return;
            
            CommandBuffer buffer = CommandBufferPool.Get(ProfilingName);
            bool colorBackedTarget = target.rt != null && target.rt.descriptor.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.None;
            var outputColorKeyword = new LocalKeyword(Materials.CopyDepth.shader, ProPixelizerKeywords.COPY_DEPTH_OUTPUT_COLOR);
            ExecutePass(
                buffer,
                source,
                target,
                IsHandleTypeDepth(Target),
                colorBackedTarget,
                Materials.CopyDepth,
                outputColorKeyword
            );

			// ...and restore transformations:
			ProPixelizerUtils.SetCameraMatrices(buffer, ref renderingData.cameraData);
			context.ExecuteCommandBuffer(buffer);
			CommandBufferPool.Release(buffer);
		}
#endif
#endregion

        #region RG
#if UNITY_2023_3_OR_NEWER

        class PassData
        {
            public TextureHandle Source;
            public TextureHandle Target;
            public bool SemanticDepthOutput;
            public bool ColorBackedTarget;
            public Material CopyDepth;
            public LocalKeyword OutputColorKeyword;
        }

        public TextureHandle GetBlitTextureRG(ContextContainer frameData, bool isSource = true)
        {
            var textureType = isSource ? Source : Target;
            var pass = isSource ? SourcePass : TargetPass;
            switch (textureType)
            {
                case BlittableHandleType.ProPixelizerColor:
                case BlittableHandleType.ProPixelizerDepth:
                    return pass.GetTargetForBlitPassRG(FromBlittableHandle(textureType), frameData);
                case BlittableHandleType.CameraDepthTexture:
                    return frameData.Get<UniversalResourceData>().cameraDepthTexture;
                case BlittableHandleType.CameraDepthAttachment:
                    //return frameData.Get<UniversalResourceData>().cameraDepth;
                    return GetFinalCameraDepthTarget(frameData);
                case BlittableHandleType.CameraColor:
                    return GetFinalCameraColorTarget(frameData);
                    //return frameData.Get<UniversalResourceData>().cameraColor;
                default:
                    throw new NotImplementedException();
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<PassData>(ProfilingName, out var passData))
            {
                passData.Source = GetBlitTextureRG(frameData, isSource: true);
                passData.Target = GetBlitTextureRG(frameData, isSource: false);
                passData.ColorBackedTarget = passData.Target.GetDescriptor(renderGraph).colorFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                passData.SemanticDepthOutput = IsHandleTypeDepth(Target);
                passData.CopyDepth = Materials.CopyDepth;
                passData.OutputColorKeyword = new LocalKeyword(passData.CopyDepth.shader, ProPixelizerKeywords.COPY_DEPTH_OUTPUT_COLOR);

                builder.UseTexture(passData.Source, AccessFlags.Read);
                builder.UseTexture(passData.Target, AccessFlags.Write);

                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    ExecutePass(
                        CommandBufferHelpers.GetNativeCommandBuffer(context.cmd),
                        data.Source,
                        data.Target,
                        data.SemanticDepthOutput,
                        data.ColorBackedTarget,
                        data.CopyDepth,
                        data.OutputColorKeyword
                        );
                });
            }
        }

#endif
#endregion

        public static void ExecutePass(
            CommandBuffer buffer,
            RTHandle Source,
            RTHandle Target,
			bool semanticDepthOutput,
            bool colorBackedTarget,
            Material copyDepth,
            LocalKeyword outputColorKeyword
            )
        {
            if (semanticDepthOutput)
            {
                if (colorBackedTarget)
                    buffer.EnableKeyword(copyDepth, outputColorKeyword);
                else
                    buffer.DisableKeyword(copyDepth, outputColorKeyword);

                copyDepth.SetFloat(ProPixelizerCopyDepthProperties.ZWrite, colorBackedTarget ? 0.0f : 1.0f);
                copyDepth.SetFloat(ProPixelizerCopyDepthProperties.ColorMask, colorBackedTarget ? (float)ColorWriteMask.Red : 0.0f);

                Blitter.BlitCameraTexture(buffer, Source, Target, copyDepth, 0);
            }
            else
                Blitter.BlitCameraTexture(buffer, Source, Target);
        }

        #region Materials
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
        #endregion
    }
}
