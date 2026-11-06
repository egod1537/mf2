// Copyright Elliot Bentine, 2018-
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System;


namespace ProPixelizer.RenderGraphResources
{
#if UNITY_2023_3_OR_NEWER
    public static class Utils
    {
        public static GraphicsFormat GetDepthFormat(in TextureDesc cameraDepthDescriptor)
        {
            Debug.Assert(GraphicsFormatUtility.IsDepthStencilFormat(cameraDepthDescriptor.format), "ProPixelizer.RenderGraphResources.ContextItem; provided cameraDepthDescriptor is not a depth format.");
            if (cameraDepthDescriptor.format == GraphicsFormat.None)
                return SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);

            return cameraDepthDescriptor.format;
        }
    }

    /// <summary>
    /// The low-resolution target in ProPixelizer
    /// </summary>
    public class LowResTargetData : ContextItem
    {
        public TextureHandle Color;
        public TextureHandle Depth;

        public override void Reset()
        {
            Color = TextureHandle.nullHandle;
            Depth = TextureHandle.nullHandle;
        }

        /// <summary>
        /// Creates a render texture descriptor for the low resolution target, given the input camera texture.
        /// </summary>
        public TextureDesc ColorDescriptor(
            TextureDesc cameraTexture,
            ProPixelizerTargetConfiguration config
            )
        {
            var colorDescriptor = cameraTexture;
            colorDescriptor.useMipMap = false;
            colorDescriptor.width = config.Width;
            colorDescriptor.height = config.Height;
            colorDescriptor.depthBufferBits = 0;
            return colorDescriptor;
        }

        public TextureDesc DepthDescriptor(
            TextureDesc cameraTexture, 
            ProPixelizerTargetConfiguration config
            )
        {
            var depthDescriptor = cameraTexture;
            depthDescriptor.useMipMap = false;
            depthDescriptor.width = config.Width;
            depthDescriptor.height = config.Height;
            depthDescriptor.format = Utils.GetDepthFormat(cameraTexture);
            return depthDescriptor;
        }
    }

    /// <summary>
    /// Pixel-expanded low resolution targets.
    /// </summary>
    public class LowResPixelExpanded : ContextItem
    {
        public TextureHandle Color;
        public TextureHandle Depth;

        public override void Reset()
        {
            Color = TextureHandle.nullHandle;
            Depth = TextureHandle.nullHandle;
        }
    }

    /// <summary>
    /// The meta-data buffer used for the ProPixelizer metadata pass.
    /// </summary>
    public class MetadataTargetData : ContextItem
    {
        public TextureHandle Color;
        public TextureHandle Depth;

        public override void Reset()
        {
            Color = TextureHandle.nullHandle;
            Depth = TextureHandle.nullHandle;
        }
    }

    /// <summary>
    /// The outline buffer used for ProPixelizer edges and outlines.
    /// </summary>
    public class OutlineTargetData : ContextItem
    {
        public TextureHandle Color;
        public override void Reset()
        {
            Color = TextureHandle.nullHandle;
        }
    }

    public class PixelizationMapData : ContextItem
    {
        public TextureHandle Map;
        public override void Reset()
        {
            Map = TextureHandle.nullHandle;
        }
    }

    public class ProPixelizerRecomposedDepthData : ContextItem
    {
        public TextureHandle Target;
        public override void Reset() => Target = TextureHandle.nullHandle;
    }

    public class ProPixelizerRecomposedColorData : ContextItem
    {
        public TextureHandle Target;
        public override void Reset() => Target = TextureHandle.nullHandle;
    }

    /// <summary>
    /// Camera targets before redirection
    /// </summary>
    public class ProPixelizerOriginalCameraTargets : ContextItem
    {
        public TextureHandle Color;
        public TextureHandle Depth;

        public override void Reset()
        {
            Color = TextureHandle.nullHandle;
            Depth = TextureHandle.nullHandle;
        }
    }

    public class ProPixelizerSubCameraTargets : ContextItem
    {
        public TextureHandle Color;
        public TextureHandle Depth;

        public override void Reset()
        {
        }
    }

    public class SceneDepthBeforeRecomposition : ContextItem
    {
        public TextureHandle Depth;

        public override void Reset()
        {
            Depth = TextureHandle.nullHandle;
        }
    }
#endif
}
