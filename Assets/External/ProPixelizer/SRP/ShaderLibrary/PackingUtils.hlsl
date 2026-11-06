// Copyright Elliot Bentine, 2018-
#ifndef PACKINGUTILS_INCLUDED
#define PACKINGUTILS_INCLUDED

// Similar functions were previously defined in UnityCG.cginc but have since been dropped in the (nearly equivalent) Core.hlsl.
// Note that the built-in versions did not work perfectly! See the below SO for details:
//	https://stackoverflow.com/questions/41566049/understanding-unitys-rgba-encoding-in-float-encodefloatrgba

inline float2 EncodeFloatRG(float v)
{
	uint vi = (uint)(v * (16.0f * 16.0f));
	int ex = (int)(vi / 16.0001 % 16.0001); // Note that imperfect division is required to prevent compiler replacing with bitExtract (not supported on ES 3.0)
	int ey = (int)(vi % 16);
	float2 e = float2(ex / 15.0f, ey / 15.0f);
	return e;
}

inline float DecodeFloatRG(float2 enc)
{
	uint ex = (uint)(enc.x * 15);
	uint ey = (uint)(enc.y * 15);
	uint v = (ex * 16) + ey;
	return v / (16.0f * 16.0f);
}

static const float PIXELMAP_DELTA_MAX = 10.0;

inline float PixelSizeToAlpha(float pixelSize) {
	return clamp(pixelSize, 0.0, 5.0) / 6.0;
}

inline float AlphaToPixelSize(float alpha) {
	return round(alpha * 6.0 % 6.0);
}

// PackPixelMap(uv, targetUV, texel_params):
//   Pack shifted UV coordinates into the pixelation map 8-bit RGBA buffer.
//
//   Input Arguments:
//    -uv: current screen uv coordinates.
//    -targetUV: the UV coordinate to sample for pixelisation.
//    -texel_params: float4 describing texel size (rg) and screen resolution (ba)
inline float4 PackPixelMap(in float2 uv, in float2 targetUV, in float4 texture_params, in float pixelSizeEncoded) {
	float2 delta = (targetUV - uv);
	float2 shifts = delta / texture_params.rg / PIXELMAP_DELTA_MAX + 0.5;
	return float4(shifts.x, shifts.y, pixelSizeEncoded, 0);
}

// UnpackPixelMap(uv, packed, texel_params):
//   Unpack UV coordinates from the pixelation map 8-bit RGBA buffer into float2 that can be used to sample a screen texture.
//
//   Input Arguments:
//    -uv: current screen uv coordinates.
//    -packed: The packed UV coordinates from the pixelation map buffer.
//    -texel_params: float4 describing texel size (rg) and screen resolution (ba)
//   Output Arguments:
//    -uv_coord: coordinate of the centre pixel
//    -pixelSize: size of this macro pixel.
inline void UnpackPixelMap(in float2 uv, in float4 packed, in float4 texture_params, out float2 uv_coord, out float pixelSize) {
	float2 shift = round((packed.xy - 0.5) * PIXELMAP_DELTA_MAX) * texture_params.xy;
	uv_coord = uv + shift;
	pixelSize = AlphaToPixelSize(packed.b);
}

inline float2 RemapNormals(float3 normalVS) {
	return normalVS.rg * 0.5 + 0.5;
} 

inline void PackMetadata(float ID, float pixelSize, float3 normalVS, out float4 output) {
	output = float4(0.0, 0.0, 0.0, 0.0);
	output.rb = RemapNormals(normalVS);
	output.g = fmod(ID, 256.0) / 256.0;
	output.a = PixelSizeToAlpha(pixelSize);
}

inline float getUID(float4 data) {
	return data.g;
}

inline float3 UnpackMetadataBufferNormal(float4 packed) {
	float2 mapped = packed.rb * 2 - 1;
	float b = sqrt(1.01 - dot(mapped, mapped));
	return float3(mapped, b);
}

inline void UnpackMetadata(float4 packed, out float3 normalVS, out float ID, out float pixelSize) {
	normalVS = UnpackMetadataBufferNormal(packed);
	ID = getUID(packed);
	pixelSize = AlphaToPixelSize(packed.a);
}

inline float4 PackOutline(float IDfactor, float edgeFactor, float3 averageNormal) {
	float4 color;
	color.rb = RemapNormals(averageNormal);
	color.g = IDfactor;
	color.a = edgeFactor;
	return color;
}

// the equivalent 'UnpackOutline' function is defined in OutlineUtils.hlsl

#endif