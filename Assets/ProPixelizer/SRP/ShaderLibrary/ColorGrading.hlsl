// Copyright Elliot Bentine, 2018-
#ifndef COLORGRADING_INCLUDED
#define COLORGRADING_INCLUDED

#define MAXCOLOR 16.0
#define RES 16.0
#define DITHER_SIZE 16.0

#pragma warning (disable : 3571)

// Calculates the dither offset in the palette LUT.
//
// Returns a number in the range [0,1], which selects a particular row from the palette LUT.
// The row is selected using the screen position of the fragment relative to the object's position,
// and accounting for the macro pixel size, such that the dither pattern will occur using
// the macropixel grid.
inline float GetDitherPaletteOffset(float2 ditherUV) {
	return (((uint(frac(ditherUV.x / 4) * 4)) * 4 + uint(frac(ditherUV.y / 4) * 4)) / DITHER_SIZE);
}

// Maps the given float input, in the range [0,1], to an internal cell range on the LUT.
// Assumes that the input float is already clamped to the range (0,1).
inline float mapToCell(float input, float size) {
	return (clamp(input * RES, 0, RES-1)+0.5) / (RES*size);
}

/// <summary>
/// Raw function for use in hlsl (rather than Shadergraph).
/// </summary>
inline void InternalColorGrade(TEXTURE2D(_palette), SAMPLER(sampler_palette), float4 orig, float2 ditherUV, out float4 graded)
{
	// Do the color grading LUT in gamma space - so the colors are more evenly spaced for the human eye, better use of LUT bit depth.
	// Note that outgoing linear->gamma is not required - the LUT is stored as sRGB texture.
#ifndef UNITY_COLORSPACE_GAMMA
	orig = pow(orig, 0.454545);
#endif
	orig = clamp(orig, 0.0, 0.9999);

	// uv within one segment of RG space.
	float u = mapToCell(orig.r, RES);
	float v = mapToCell(orig.g, DITHER_SIZE);

	// select cell using b
	float cell = round(clamp(orig.b * MAXCOLOR, 0.0, RES - 1.0));
	u = cell / RES + u;

	// Use ditherUV to select palette row.
	v = v + GetDitherPaletteOffset(ditherUV);


	// Perform the LUT
	graded = SAMPLE_TEXTURE2D(_palette, sampler_palette, float2(u, v));

	//Visualise dither pattern indices:
	//graded.rgb = GetDitherPaletteOffset(ditherUV * _ScreenParams.xy) * float3(1,1,1);
}

inline void ColorGrade_float(UnityTexture2D _palette, UnitySamplerState sampler_palette, float4 orig, float2 ditherUV, out float4 graded)
{
	InternalColorGrade(_palette.tex, sampler_palette.samplerstate, orig, ditherUV, graded);
}

#endif