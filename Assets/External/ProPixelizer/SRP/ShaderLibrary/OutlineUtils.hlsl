// Copyright Elliot Bentine, 2018-
//
// Utility functions for outlining.
#ifndef OUTLINE_UTILS_INCLUDED
#define OUTLINE_UTILS_INCLUDED

TEXTURE2D(_ProPixelizerOutlines);
SAMPLER(sampler_ProPixelizerOutlines);
float _ProPixelizer_BackBufferFlag;

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Version.hlsl"
#include "ShaderGraphUtils.hlsl"
#include "PackingUtils.hlsl"

/// <summary>
/// Gets and unpacks values from the outline buffer.
/// </summary>
inline void GetOutline_float(float2 texel, out float IDOutline, out float EdgeOutline, out float3 EdgeNormalVS) {
#if SHADERGRAPH_PREVIEW_TEST
	//disable outlines in shadergraph preview - we don't have the passes to make them work anyway.
	IDOutline = 0;
	EdgeOutline = 0;
	EdgeNormalVS = float3(0,0,1);
#else
	#if UNITY_UV_STARTS_AT_TOP
		if (_ProPixelizer_BackBufferFlag > 0.1)
		{
			texel.y = 1 - texel.y;
		}
	#endif
	float4 packed = SAMPLE_TEXTURE2D(_ProPixelizerOutlines, sampler_ProPixelizerOutlines, texel);
	IDOutline = packed.g;
	EdgeOutline = packed.a;
	EdgeNormalVS = UnpackMetadataBufferNormal(packed);
#endif
}
#endif