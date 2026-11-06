// Copyright Elliot Bentine, 2018-
// 
// Applies full-screen color grading to the primary Blitter source texture.
Shader "Hidden/ProPixelizer/SRP/FullscreenColorGrade" {
	Properties{
		_ColorGradingLUT("Color Grading LUT", 2D) = "" {}
	}

	SubShader{
	Tags{
		"RenderType" = "Opaque"
		"PreviewType" = "Plane"
		"RenderPipeline" = "UniversalPipeline"
	}

	Pass{
		Cull Off
		ZWrite On
		ZTest Off
		Blend Off

		HLSLPROGRAM 
		#pragma vertex vert
		#pragma fragment frag
		// I don't like that this has to be global, but setting local keywords from buffer is broken on 2022.2
		#pragma multi_compile PROPIXELIZER_PIXEL_EXPANSION _
		#pragma multi_compile PROPIXELIZER_FULL_SCENE _
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "ColorGrading.hlsl"
		#include "PackingUtils.hlsl"
		#include "ScreenUtils.hlsl"
		#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

		// URP 14 supplies this value when binding _BlitTexture, but does not declare it in Blit.hlsl.
		// The declaration was moved into Blit.hlsl in Unity 6.
		#if UNITY_VERSION < 600000
		float4 _BlitTexture_TexelSize;
		#endif

		TEXTURE2D(_PixelizationMap);
		TEXTURE2D(_ColorGradingLUT);
		TEXTURE2D(_ProPixelizer_Metadata);
		float4 _ColorGradingDitherPatternOffset;

		struct ProPVaryings {
			float4 pos : SV_POSITION;
			float4 scrPos:TEXCOORD1;
		};

		ProPVaryings vert(Attributes v) {
			Varyings vars;
			vars = Vert(v);
			ProPVaryings o;
			o.pos = vars.positionCS;
			o.scrPos = float4(ComputeNormalizedDeviceCoordinatesWithZ(o.pos.xyz).xyz, 0);
			return o;
		}

		void frag(ProPVaryings i, out float4 color : SV_Target) {

			// # Full screen color grading: 
			// 
			// - If pixel expansion is used, we always want the dither pattern to respect that (use low-res texel coordinates and pixel size).
			// - If the low-res target is used, we want dither pattern to respect that (use low-res texel coordinates).
			// - Otherwise, use screen coordinates.
			
			float2 lowResUV = i.scrPos.xy;
			float2 roundedLowResPixelPos = floor(lowResUV * _ProPixelizer_RenderTargetInfo.xy) + 0.5;
			float2 roundedLowResUV = roundedLowResPixelPos / _ProPixelizer_RenderTargetInfo.xy;
			#if PROPIXELIZER_FULL_SCENE
				float2 screenPixelPos = roundedLowResPixelPos; //floor(roundedLowResPixelPos * _ProPixelizer_RenderTargetInfo.xy + 0.1);
			#else
				float2 screenPixelPos = floor(i.scrPos.xy * _BlitTexture_TexelSize.zw + 0.1); //use screen position
			#endif

			#if PROPIXELIZER_PIXEL_EXPANSION
				// Check the metadata buffer to get the pixel Size to use for the dither patterns where pixels are expanded.
				float4 packed = SAMPLE_TEXTURE2D(_PixelizationMap, sampler_PointClamp, lowResUV);
				float2 pixelCentreLowResUV;
				float pixelSize;
				float4 textureParams = float4(1 / _ProPixelizer_RenderTargetInfo.x, 1 / _ProPixelizer_RenderTargetInfo.y, 1, 1);
				UnpackPixelMap(roundedLowResUV, packed, textureParams, pixelCentreLowResUV, pixelSize);
				float2 lowResPixelPos = pixelCentreLowResUV * _ProPixelizer_RenderTargetInfo.xy / pixelSize;
				float2 pixelPos = pixelSize > 0.5 ? lowResPixelPos : screenPixelPos;
			#else
				// Although pixel size is always one, we need to determine if we should use texels from the low-res target
				// or screen target as position.
				float4 packed = SAMPLE_TEXTURE2D(_ProPixelizer_Metadata, sampler_PointClamp, lowResUV);
				float3 normalCS;
				float ID, pixelSize;
				UnpackMetadata(packed, normalCS, ID, pixelSize);
				#if PROPIXELIZER_FULL_SCENE
					// If full scene, make sure we always use low-res target position.
					float2 pixelPos = roundedLowResPixelPos;
				#else
					// If hybrid mode, decide based on whether current pixel is from low-res or high-res scene.
					float2 pixelPos = pixelSize > 0.5 ? roundedLowResPixelPos : screenPixelPos;
				#endif
			#endif
			float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.scrPos.xy);
			
			float2 delta = pixelPos + _ColorGradingDitherPatternOffset.xy + 0.01;
			float2 ditherUV = delta + float2(0.5, 0.5);

			float4 gradedColor;
			InternalColorGrade(_ColorGradingLUT, sampler_PointClamp, sceneColor, ditherUV, gradedColor);
			color = gradedColor;

			//// DEBUG: dither offsets (overlays UVs):
			//float plaqueSize = 15;
			//color = float4(
			//	fmod(pixelPos.x, plaqueSize),
			//	fmod(pixelPos.y, plaqueSize),
			//	0, 0) / plaqueSize;
			//color = 0.4 * color + sceneColor * 0.6;
		}
		ENDHLSL
		}
	}
	FallBack "Diffuse"
}
