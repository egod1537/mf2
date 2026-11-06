// Copyright Elliot Bentine, 2018-
// Helper functions for screen parameters.
// Some of these parameters have not yet been exposed as Shader Graph nodes, so this file is needed.

#ifndef SCREEN_UTIL_INCLUDED
#define SCREEN_UTIL_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Version.hlsl"
#include "ShaderGraphUtils.hlsl"

float4 _ProPixelizer_RenderTargetInfo;
float _ProPixelizer_VirtualCameraLowResDraw;

void GetScaledScreenParameters_float(out float4 Out)
{
	#if SHADERGRAPH_PREVIEW_TEST
		Out = float4(0, 0, 0, 0);
	#else
	Out = _ProPixelizer_RenderTargetInfo;
	#endif
}

float4 _ProPixelizer_ScreenTargetInfo;

float GetProPixelizerLowResTargetMargin() {
	return 5.0;
}

void GetScreenTargetParameters_float(out float4 Out)
{
#if SHADERGRAPH_PREVIEW_TEST
	Out = float4(0, 0, 0, 0);
#else
	Out = _ProPixelizer_ScreenTargetInfo;
#endif

	// Note that there are Unity properties for this, e.g. _ScaledScreenParams,
	// and functions to use them, see e.g.
	// https://github.com/Unity-Technologies/Graphics/blob/632f80e011f18ea537ee6e2f0be3ff4f4dea6a11/Packages/com.unity.shadergraph/Editor/Generation/Targets/BuiltIn/ShaderLibrary/ShaderVariablesFunctions.hlsl
	// However, also note that I have found them to be unreliable: https://forum.unity.com/threads/_scaledscreenparameters-and-render-target-subregion.1336277/
}

float4 _ProPixelizer_LowResCameraDeltaUV;
float4 _ProPixelizer_OrthoSizes;

inline float2 GetLowResCameraDeltaUV() {
	return float2(_ProPixelizer_LowResCameraDeltaUV.xy);
}

/// <summary>
/// Gets the scale used to convert a UV-space offset in the low-resolution target
/// into the equivalent offset in the full-resolution screen target.
/// </summary>
inline float2 GetLowResolutionTargetToScreenUVScale() {
	if (unity_OrthoParams.w > 0.5) {
		return float2(
			_ProPixelizer_OrthoSizes.x / _ProPixelizer_OrthoSizes.z,
			_ProPixelizer_OrthoSizes.y / _ProPixelizer_OrthoSizes.w
			);
	}
	else {
		return float2(
			_ProPixelizer_RenderTargetInfo.x / (_ProPixelizer_RenderTargetInfo.x - 2 * GetProPixelizerLowResTargetMargin()),
			_ProPixelizer_RenderTargetInfo.y / (_ProPixelizer_RenderTargetInfo.y - 2 * GetProPixelizerLowResTargetMargin())
			);
	}
}

/// <summary>
/// Converts a UV in the full-resolution screen target into the corresponding UV
/// in ProPixelizer's low-resolution target.
/// </summary>
inline float2 ConvertScreenToLowResolutionTargetUV(float2 uv) {
	float2 scale = rcp(GetLowResolutionTargetToScreenUVScale());
	if (unity_OrthoParams.w > 0.5) {
		// Orthographic: account for the snapped low-resolution camera position.
		return GetLowResCameraDeltaUV() + 0.5 + (uv - 0.5) * scale;
	}
	else {
		// Perspective: map the screen inside the low-resolution target margins.
		return 0.5 + (uv - 0.5) * scale;
	}
}


// Backwards-compatible alias for ConvertScreenToLowResolutionTargetUV.
inline float2 ConvertToLowResolutionTargetUV(float2 uv) {
	return ConvertScreenToLowResolutionTargetUV(uv);
}

/// <summary>
/// Converts a UV in ProPixelizer's low-resolution target into the corresponding
/// UV in the full-resolution screen target.
/// </summary>
inline float2 ConvertLowResolutionTargetToScreenUV(float2 uv) {
	float2 scale = GetLowResolutionTargetToScreenUVScale();
	if (unity_OrthoParams.w > 0.5) {
		float2 lowResDeltaUV = GetLowResCameraDeltaUV();
		return 0.5 + (uv - lowResDeltaUV - 0.5) * scale;
	}
	else {
		return 0.5 + (uv - 0.5) * scale;
	}
}

inline float4 TransformLowResPixelPosToScreenPixelPos(float4 pixelPosition) {
	float2 lowResUV = float2(
		pixelPosition.x / _ProPixelizer_RenderTargetInfo.x,
		1.0f - pixelPosition.y / _ProPixelizer_RenderTargetInfo.y
		);
	float2 screenUV = ConvertLowResolutionTargetToScreenUV(lowResUV);
	float4 screenPixelPos = float4(
		screenUV.x * _ProPixelizer_ScreenTargetInfo.x,
		(1.0f - screenUV.y) * _ProPixelizer_ScreenTargetInfo.y,
		pixelPosition.z,
		pixelPosition.w
		);
	return screenPixelPos;
}

/// <summary>
/// Clamps a pixel position to valid texel centres in the full-resolution screen target.
/// Low-resolution rendering includes an overscan margin, so transformed positions can
/// legitimately fall outside the screen even though screen-space textures cannot be loaded there.
/// </summary>
inline float4 ClampScreenPixelPosToScreenTarget(float4 pixelPosition) {
	float2 minPixelPosition = float2(0.5f, 0.5f);
	float2 maxPixelPosition = max(minPixelPosition, _ProPixelizer_ScreenTargetInfo.xy - minPixelPosition);
	pixelPosition.xy = clamp(pixelPosition.xy, minPixelPosition, maxPixelPosition);
	return pixelPosition;
}
#endif
