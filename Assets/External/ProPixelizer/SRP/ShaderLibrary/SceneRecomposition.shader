// Copyright Elliot Bentine, 2018-
// 
// Part of the ProPixelizer Package.
// 
// This shader recomposits a low-resolution target back into the high resolution screen target. The recomposition
// accounts for dissimilarity between the low-res and main scene camera projection, allowing smooth sub-pixel motion
// of the camera.
//
// When recomposing we also use a pixel art optimised AA filter, which is inspired by the work of Cole Cecil: 
//     https://colececil.io/blog/2017/scaling-pixel-art-without-destroying-it/
// 
// # Recomposition modes
// 
// The following recomposition modes are available:
// - RECOMPOSITION_DEPTH_BASED: The primary texture is blended with the secondary texture by comparing the depth
//                              buffers and selecting the nearest of the two.
// - RECOMPOSITION_ONLY_SECONDARY: Only the secondary target is used when recompositing the scene.
//
// The local keyword DEPTH_OUTPUT_ON can be used to enable or disable depth output.
// - All modes: accounts for dissimilarity between the low-res and main scene camera projection,
//     which allows smooth sub-(low-res) pixel camera movement at the screen resolution.
//
//
// Implementation Notes for the AA filter:
//
// There are two cases that occur when upscaling:
//   1. A pixel is entirely within a texel. In this case, we just take the texel color (sample at texel centre).
//   2. A pixel partially covers multiple texels. In this case, we take a weighted sample of the surrounding texels.
// Both can be elegantly described by taking a transfer function for the lowResCoordinate:
// 
// ^  (uv)
// |           ---
// |          /
// |      ----
// |     /
// |  ---
// x--------------> (in)
// 
// This blends the uvs between texels for intermediate pixels, and snaps to texel centres for completely contained pixels.
// The width of an 'edge' `/` in texel space is 'texels_per_pixel' / _RenderTargetInfo.xy. 
// (Note that because we are only blitting two rectangles together, of same orientation, we don't need
// to consider rotational effects that are required more generally).


Shader "Hidden/ProPixelizer/SRP/Internal/SceneRecomposition" {
	Properties{
	_BlitTexture("Texture", any) = "" {}
	_InputDepthTexture("Texture", any) = "" {}
	_SecondaryTexture("Texture", any) = "" {}
	_SecondaryDepthTexture("Texture", any) = "" {}
	}

	SubShader {
		Pass {
			ZTest Off Cull Off ZWrite On

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#pragma multi_compile DEPTH_OUTPUT_ON _
			#pragma multi_compile RECOMPOSITION_DEPTH_BASED RECOMPOSITION_ONLY_SECONDARY
			#pragma multi_compile PIXELART_AA_FILTER_ON _

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			TEXTURE2D_X_FLOAT(_InputDepthTexture);
			SAMPLER(sampler_InputDepthTexture_point_clamp);
			TEXTURE2D_X_FLOAT(_SecondaryTexture);
			
			// Pixel art AA filtering is supported for color targets.
			// It does not occur during shadergraph preview testing, nor depth output paths which are point sampled.
			#define USING_PIXELART_FILTER \
						(PIXELART_AA_FILTER_ON && !SHADERGRAPH_PREVIEW_TEST) // && !DEPTH_OUTPUT_ON)

			#if UNITY_REVERSED_Z
				#define GET_DEPTH_DELTA(depthA, depthB) ((depthA) - (depthB))
				#define FURTHEST_RAW_DEPTH(a, b) min((a), (b))
			#else
				#define GET_DEPTH_DELTA(depthA, depthB) ((depthB) - (depthA))
				#define FURTHEST_RAW_DEPTH(a, b) max((a), (b))
			#endif
			
			#if USING_PIXELART_FILTER
				#define PIXELART_SAMPLER sampler_linear_clamp
			#else
				#define PIXELART_SAMPLER sampler_point_clamp
			#endif
			SAMPLER(PIXELART_SAMPLER);

			#if RECOMPOSITION_DEPTH_BASED && !DEPTH_OUTPUT_ON
				SAMPLER(sampler_SecondaryTexture_point_clamp);
			#endif
			TEXTURE2D_X_FLOAT(_SecondaryDepthTexture);
			SAMPLER(sampler_SecondaryDepthTexture_point_clamp);

			struct DBCVaryings {
				float4 vertex : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			DBCVaryings vert(Attributes v) {
				Varyings vars;
				vars = Vert(v);
				DBCVaryings o;
				o.vertex = vars.positionCS;
				o.texcoord = vars.texcoord;
				return o;
			}

			#include "ScreenUtils.hlsl"

		#if RECOMPOSITION_DEPTH_BASED
			/// Compares the depths at the given lowRes (_SecondaryDepth) and scene (_InputDepth) coordinates, 
			/// and then samples 
			#if DEPTH_OUTPUT_ON
				#define RESOLVEDEPTH_RETURN_TYPE float
			#else
				#define RESOLVEDEPTH_RETURN_TYPE float4
			#endif

			inline RESOLVEDEPTH_RETURN_TYPE ResolveDepthRecomposedSample(
				float2 lowResUV,
				float2 sceneUV)
			{
				float lowResDepth = SAMPLE_TEXTURE2D_X(
					_SecondaryDepthTexture,
					sampler_SecondaryDepthTexture_point_clamp,
					lowResUV).r;

				float sceneDepth = SAMPLE_TEXTURE2D_X(
					_InputDepthTexture,
					sampler_InputDepthTexture_point_clamp,
					sceneUV).r;

				if (GET_DEPTH_DELTA(sceneDepth, lowResDepth) < 0.0)
					#if DEPTH_OUTPUT_ON
						return lowResDepth;
					#else 
						return SAMPLE_TEXTURE2D_X(
							_SecondaryTexture,
							sampler_SecondaryTexture_point_clamp,
							lowResUV);
					#endif
				else
					#if DEPTH_OUTPUT_ON
						return sceneDepth;
					#else
						return SAMPLE_TEXTURE2D_X(
							_BlitTexture,
							PIXELART_SAMPLER,
							sceneUV);
					#endif
			}
		#endif

		#if DEPTH_OUTPUT_ON
			void frag(DBCVaryings i, out float output : SV_Depth) {
		#else
			void frag(DBCVaryings i, out float4 output : SV_Target) {
		#endif
				float secondaryDepth;
				float2 lowResSampleUV;

				// Calculate the UV coordinates used for sampling the low-res texture (_Secondary).
				#if USING_PIXELART_FILTER
					float2 pixelsPerTexel = _ProPixelizer_ScreenTargetInfo.xy / _ProPixelizer_RenderTargetInfo.xy;
					float2 lowResCoordinate = ConvertScreenToLowResolutionTargetUV(i.texcoord) * _ProPixelizer_RenderTargetInfo.xy; // n+0.5 on texel centres.
					float2 lowResTexelFrac = frac(lowResCoordinate);
					float2 lowResTexelBase = floor(lowResCoordinate);
					float2 lowResTexelOffset = clamp(lowResTexelFrac * pixelsPerTexel, 0, 0.5) + clamp((lowResTexelFrac - 1) * pixelsPerTexel + 0.5, 0.0, 0.5);

					#if RECOMPOSITION_ONLY_SECONDARY
						// A linear sampler performs the interpolation for full-scene recomposition.
						lowResSampleUV = (lowResTexelBase + lowResTexelOffset) / _ProPixelizer_RenderTargetInfo.xy;
					#elif RECOMPOSITION_DEPTH_BASED
						// Depth-based recomposition conditionally includes neighbours, so begin at the
						// centre of the low-res texel and perform the interpolation manually below.
						lowResSampleUV = (lowResTexelBase + 0.5) / _ProPixelizer_RenderTargetInfo.xy;
					#endif
				#else
					lowResSampleUV = ConvertScreenToLowResolutionTargetUV(i.texcoord);
				#endif

				#if RECOMPOSITION_ONLY_SECONDARY
					#if DEPTH_OUTPUT_ON
						output = SAMPLE_TEXTURE2D_X(
							_SecondaryDepthTexture,
							sampler_SecondaryDepthTexture_point_clamp,
							lowResSampleUV).r;
					#else
						output = SAMPLE_TEXTURE2D_X(
							_SecondaryTexture,
							PIXELART_SAMPLER,
							lowResSampleUV);
					#endif
					return;

				#elif RECOMPOSITION_DEPTH_BASED

					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
					#if !USING_PIXELART_FILTER 
						// When _not_ using pixel art filter, we only care about the lowResSampleUV.
						// Test the scene and low res (Input and Secondary textures)
						output = ResolveDepthRecomposedSample(lowResSampleUV, i.texcoord);
					#else
						// When using the AA pixel art filter and depth-based recomposition, we need to sample adjacent
						// low-res texel neighbours (creating a 2x2 sample for interpolation). Each low-res texel is then
						// depth-tested to determine whether to use low-res or scene colour, and we combine the result
						// in accordance with the pixel art AA filtering.
						float2 lowResTexelSize = rcp(_ProPixelizer_RenderTargetInfo.xy);
						float2 neighbourDirection = step(0.5, lowResTexelOffset) * 2.0 - 1.0;
						float2 baseTexelSceneUV = ConvertLowResolutionTargetToScreenUV(lowResSampleUV);
						float2 scale = GetLowResolutionTargetToScreenUVScale();
						float2 xOffset = float2(neighbourDirection.x * lowResTexelSize.x, 0.0);
						float2 yOffset = float2(0.0, neighbourDirection.y * lowResTexelSize.y);

						RESOLVEDEPTH_RETURN_TYPE depthRecomposedSamples[4];
						depthRecomposedSamples[0] = ResolveDepthRecomposedSample(
							lowResSampleUV,
							baseTexelSceneUV);

						depthRecomposedSamples[1] = ResolveDepthRecomposedSample(
							lowResSampleUV + xOffset,
							baseTexelSceneUV + xOffset * scale);

						depthRecomposedSamples[2] = ResolveDepthRecomposedSample(
							lowResSampleUV + yOffset,
							baseTexelSceneUV + yOffset * scale);

						depthRecomposedSamples[3] = ResolveDepthRecomposedSample(
							lowResSampleUV + xOffset + yOffset,
							baseTexelSceneUV + (xOffset + yOffset) * scale);

						float2 interpolation_weight = abs(lowResTexelOffset - 0.5);
						#if !DEPTH_OUTPUT_ON
							// Interpolate the samples using the pixel art filter
							float4 interpolated_base_row = lerp(depthRecomposedSamples[0], depthRecomposedSamples[1], interpolation_weight.x);
							float4 interpolated_neighbour_row = lerp(depthRecomposedSamples[2], depthRecomposedSamples[3], interpolation_weight.x);
							output = lerp(interpolated_base_row, interpolated_neighbour_row, interpolation_weight.y);
						#else
							output = depthRecomposedSamples[0];
							if (interpolation_weight.x > 0.0)
								output = FURTHEST_RAW_DEPTH(output, depthRecomposedSamples[1]);
							if (interpolation_weight.y > 0.0)
								output = FURTHEST_RAW_DEPTH(output, depthRecomposedSamples[2]);
							if (interpolation_weight.x > 0.0 && interpolation_weight.y > 0.0)
								output = FURTHEST_RAW_DEPTH(output, depthRecomposedSamples[3]);
						#endif
					#endif
				#endif
			}
			ENDHLSL
		}
	}
}
