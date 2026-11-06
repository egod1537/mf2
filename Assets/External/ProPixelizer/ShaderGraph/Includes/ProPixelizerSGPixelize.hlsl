// Copyright Elliot Bentine 2018-

// This file wraps functionality from PixelUtils to allow inclusion in the ProPixelizer shadergraph subtarget.

inline void ProPixelizerPixelation_float(float alpha_in, float4 ScreenPosition_Raw, float alpha_clip_threshold, float alpha_out, out float2 ditherUV) {

#if defined(USE_OBJECT_POSITION_FOR_PIXEL_GRID_ON)
	float3 objectCentreWS = SHADERGRAPH_OBJECT_POSITION;
#else
	float3 objectCentreWS = _PixelGridOrigin.xyz;
#endif

	float4 screenParams;
	GetScaledScreenParameters_float(screenParams);
	
	float2 posCS = float4(ScreenPosition_Raw.rg / ScreenPosition_Raw.a * screenParams.rg, 0, 0);
	
	PixelClipAlpha_float(UNITY_MATRIX_VP, objectCentreWS, screenParams, posCS, _PixelSize, alpha_in, alpha_clip_threshold, alpha_out, ditherUV);
}