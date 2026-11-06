// Restores sampler behaviour in 2022.3; see Patch2022_UseFullSamplerPrecision
#if defined(PROPIXELIZER_RESTORE_TEXTURE2D_X)
	#undef TEXTURE2D_X

	// Basically reimplementing the macros in Core.hlsl
	#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
		#define TEXTURE2D_X(textureName) TEXTURE2D_ARRAY(textureName)
	#else
		#define TEXTURE2D_X(textureName) TEXTURE2D(textureName)
	#endif

	#undef PROPIXELIZER_RESTORE_TEXTURE2D_X
#endif
