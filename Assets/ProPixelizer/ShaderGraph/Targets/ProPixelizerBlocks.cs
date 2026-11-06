// Copyright Elliot Bentine, 2018-
using UnityEditor.ShaderGraph;

namespace ProPixelizer.Universal.ShaderGraph
{
	static class ProPixelizerBlockFields
	{
		/// <summary>
		/// Block fields used by the ProPixelizer SubTarget
		/// </summary>
		[GenerateBlocks]
		public struct ProPixelizerSurfaceDescription
		{
			public static string name = "ProPixelizerSurfaceDescription";
            public static BlockFieldDescriptor OutlineColor = new BlockFieldDescriptor(name, "ProPixelizerOutlineColor", "ProPixelizer Outline", "PROPIXELIZERSURFACEDESCRIPTION_OUTLINECOLOR",
				new ColorRGBAControl(UnityEngine.Color.black), ShaderStage.Fragment);
            public static BlockFieldDescriptor EdgeHighlight = new BlockFieldDescriptor(name, "ProPixelizerEdgeHighlight", "ProPixelizer Edge Highlight", "PROPIXELIZERSURFACEDESCRIPTION_EDGEHIGHLIGHT",
				new ColorRGBAControl(UnityEngine.Color.black), ShaderStage.Fragment);
            public static BlockFieldDescriptor EdgeBevelWeight = new BlockFieldDescriptor(name, "ProPixelizerBevelWeight", "ProPixelizer Bevel Weight", "PROPIXELIZERSURFACEDESCRIPTION_EDGEHIGHLIGHT",
                new FloatControl(0f), ShaderStage.Fragment);
        }

        public static BlockFieldDescriptor[] GetProPixelizerSubTargetBlockFields()
        {
            return new BlockFieldDescriptor[] {
                BlockFields.SurfaceDescription.BaseColor,
                BlockFields.SurfaceDescription.Smoothness,
                BlockFields.SurfaceDescription.NormalTS,
                BlockFields.SurfaceDescription.Emission,
                BlockFields.SurfaceDescription.Occlusion,
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
                ProPixelizerSurfaceDescription.OutlineColor,
                ProPixelizerSurfaceDescription.EdgeHighlight,
                ProPixelizerSurfaceDescription.EdgeBevelWeight
            };
        }
    }
}
