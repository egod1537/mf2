// ProPixelizer needs to be able to sample the depth buffer at high precision during scene recomposition.
// Otherwise, we get staircasing and banding artefacts which are especially obvious when drawing transparents.
// 
// In 2022.3, Unity's blit API does not provide a way to specify high-precision samplers. Unity 6 and onward
// added the `USE_FULL_PRECISION_BLIT_TEXTURE` define, but we don't get that utility here. So instead we 
// redefine URP's sampler macros so that Blit.hlsl will build full-precision samplers instead of medium/low-precision
// samplers.
#if UNITY_VERSION >= 202230 && UNITY_VERSION < 202300
	#define PROPIXELIZER_RESTORE_TEXTURE2D_X
	#undef TEXTURE2D_X
	#define TEXTURE2D_X(textureName) TEXTURE2D_X_FLOAT(textureName)
#endif
