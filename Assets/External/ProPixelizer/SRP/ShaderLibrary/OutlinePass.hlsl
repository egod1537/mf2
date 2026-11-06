// Copyright Elliot Bentine, 2018-
//
// vertex and fragment shaders for outline pass.
#ifndef OUTLINE_PASS_INCLUDED
#define OUTLINE_PASS_INCLUDED

SAMPLER(sampler_BaseMap_point_repeat); // this matches that used for the shadergraph, so that alpha values in both passes match.

struct Attributes
{
	float4 positionOS : POSITION; // vertex position
	float2 uv0 : TEXCOORD0; // texture coordinate
	float3 normalOS : NORMAL; // object space normals 
};

struct Varyings {
	float4 positionCS : SV_POSITION;
	float2 texCoord0 : TEXCOORD0; //texture coordinate
	float3 normalVS : NORMAL; // outline pass normals
};

Varyings outline_vert(
	Attributes input
)
{
	Varyings output = (Varyings)0;
	VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
	output.positionCS = float4(vertexInput.positionCS);
	float4x4 viewMat = GetWorldToViewMatrix();
	output.normalVS = TransformWorldToViewDir(TransformObjectToWorldNormal(input.normalOS));
	output.texCoord0 = TRANSFORM_TEX(input.uv0, _BaseMap);
	return output;
}

void outline_frag(Varyings i, out float4 color : COLOR)
{
	float alpha_out;
	float2 dither_uv;
	float4 screenParams;
	float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap_point_repeat, i.texCoord0).a * _BaseColor.a;
	GetScaledScreenParameters_float(screenParams);
	float4 pixelPos = float4(i.positionCS.xy, 0.0, 0.0);
#if defined(USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON)
	// This is equivalent to SHADERGRAPH_OBJECT_POSITION defined in Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl
	float3 objectPixelPos = UNITY_MATRIX_M._m03_m13_m23;
	PixelClipAlpha_float(UNITY_MATRIX_VP, objectPixelPos, screenParams, pixelPos, _PixelSize, alpha, _AlphaClipThreshold, alpha_out, dither_uv);
#else
	PixelClipAlpha_float(UNITY_MATRIX_VP, _PixelGridOrigin.xyz, screenParams, pixelPos, _PixelSize, alpha, _AlphaClipThreshold, alpha_out, dither_uv);
#endif 
	clip(alpha_out - 0.1);
	PackMetadata(_ID, max(1, round(_PixelSize * _ProPixelizer_Pixel_Scale)), i.normalVS, color);
}

#endif
