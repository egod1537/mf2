// Copyright Elliot Bentine, 2018-
Shader "Hidden/ProPixelizer/SRP/OutlineDetection" {
	Properties{
		_OutlineDepthTestThreshold("Threshold used for depth testing outlines.", Float) = 0.0001
		_BlitTexture("Texture", any) = "" {}
		_BlitTexture_Depth("Texture", any) = "" {}
		_NormalEdgeDetectionSensitivity("Detection threshold for normal-based edge detection.", Float) = 0.0
		_TexelSize("Low-res target texel sizes", Vector) = (0, 0, 0, 0)
	}

		SubShader{
		Tags{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
			"PreviewType" = "Plane"
		}

		Pass{
			Cull Off
			ZWrite On
			ZTest Off

			HLSLINCLUDE
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
				#include "PixelUtils.hlsl"
				#include "PackingUtils.hlsl"
				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"		
			ENDHLSL

			HLSLPROGRAM
			#pragma target 2.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_local DEPTH_TEST_OUTLINES_ON _
			#pragma multi_compile NORMAL_EDGE_DETECTION_ON _
			#pragma multi_compile_local DEPTH_TEST_NORMAL_EDGES_ON _

			#if DEPTH_TEST_OUTLINES_ON
			float _OutlineDepthTestThreshold;
			#endif

			#if NORMAL_EDGE_DETECTION_ON
			float _NormalEdgeDetectionSensitivity;
			#endif

			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			SAMPLER(sampler_BlitTexture_point_clamp);
			float4 _TexelSize;
			TEXTURE2D_X_FLOAT(_BlitTexture_Depth);
			SAMPLER(sampler_BlitTexture_Depth_point_clamp);

			float4x4 _ProPixelizer_LowRes_I_V;
			float4x4 _ProPixelizer_LowRes_I_P;

			struct ProPVaryings {
				float4 pos : SV_POSITION;
				float4 scrPos : TEXCOORD1;
			};

			#define DEPTH_USED DEPTH_TEST_OUTLINES_ON || DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
			

			ProPVaryings vert(Attributes v) {
				Varyings vars;
				vars = Vert(v);
				ProPVaryings o;
				o.pos = vars.positionCS;
				o.scrPos = float4(ComputeNormalizedDeviceCoordinatesWithZ(o.pos.xyz).xyz, 0);
				return o;
			}

			inline float getEyeDepth(float depthRaw) {
				if (unity_OrthoParams.w > 0.5) { // ortho
					#if defined(UNITY_REVERSED_Z)
						return lerp(_ProjectionParams.y, _ProjectionParams.z, depthRaw);
					#else
						return lerp(_ProjectionParams.z, _ProjectionParams.y, depthRaw);
					#endif
				}
				else { 
					return LinearEyeDepth(depthRaw, _ZBufferParams);
				}
			}

			/// <summary>
			/// Encapsulates all information about a pixel in the low-res metadata target.
			/// 
			/// Zero/low cost fields are always declared and filled.
			/// Expensive fields (samples, calculations) are only declared/filled when keywords are met.
			/// </summary>
			struct Pixel {
				// ID for ID-based outlines
				float ID;

				// UV coordinate of this neighbour in the target.
				float2 texel;

				// Integer pixel size
				float pixelSize;
				
				// Normal in view space
				float3 normalVS;

				// Offset to neighbour in clip space
				float2 deltaCS;

				#if DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
				// Normal in world space
				float3 normalWS;
				#endif

				#if DEPTH_USED
				// Raw depth as sampled from the depth buffer
				float depth;
				#endif

				#if DEPTH_TEST_NORMAL_EDGES_ON
				// View space depth in world units
				float depthVS;
				#endif

				#if DEPTH_TEST_NORMAL_EDGES_ON
				// The world space position. Only populated for 'us'.
				float3 positionWS;
				#endif
			};

			inline Pixel buildPixel(float2 texel, float2 deltaCS, float4 packed) {
				Pixel nbr = (Pixel)0;
				nbr.deltaCS = deltaCS;
				nbr.texel = texel;
				UnpackMetadata(packed, nbr.normalVS, nbr.ID, nbr.pixelSize);
				#if DEPTH_USED
					nbr.depth = SAMPLE_TEXTURE2D_X(_BlitTexture_Depth, sampler_BlitTexture_point_clamp, texel).r;
				#endif
				#if	DEPTH_TEST_NORMAL_EDGES_ON || NORMAL_EDGE_DETECTION_ON
					nbr.normalWS = mul(_ProPixelizer_LowRes_I_V, float4(nbr.normalVS.rgb, 0)).xyz;
				#endif
				#if DEPTH_TEST_NORMAL_EDGES_ON
					nbr.depthVS = getEyeDepth(nbr.depth);
				#endif
				return nbr;
			}

			/// <summary>
			/// Samples packed data and depth data for the pixel located 'neighbour' pixels from the main texel.
			/// </summary>
			inline Pixel samplePixel(float2 mainTexel, float2 neighbour, float pixelSize) {
				
				float2 deltaCS = float2(neighbour.x * _TexelSize.x * pixelSize, neighbour.y * _TexelSize.y * pixelSize);
				float2 texel = mainTexel + deltaCS;
				float4 packed = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture_point_clamp, texel);
				return buildPixel(texel, deltaCS, packed);
			}

			/// <summary>
			/// Perform an ID comparison between two pixels.
			/// </summary>
			inline float compareIDs(Pixel us, Pixel nbr) {
				#if DEPTH_TEST_OUTLINES_ON
					#if UNITY_REVERSED_Z
						bool neighbourInFront = nbr.depth > us.depth + _OutlineDepthTestThreshold;
					#else
						bool neighbourInFront = nbr.depth < us.depth - _OutlineDepthTestThreshold;
					#endif
					return neighbourInFront || (nbr.ID == us.ID && nbr.pixelSize > 0.5) ? 1 : 0;
				#else
					return nbr.ID == us.ID && nbr.pixelSize > 0.5 ? 1 : 0;
				#endif
			}

			/// <summary>
			/// Gets the world space position for a given screen UV and raw depth.
			/// </summary>
			inline float3 getWorldSpacePosition(float2 texel, float depthRaw) {
				float4 posCS = float4(texel * 2 - 1, 1, 1); // xy in range (-1, 1) across screen
				float4 temp = mul(_ProPixelizer_LowRes_I_P, posCS);
				float4 posVS;
				float depthES = getEyeDepth(depthRaw);
				if (unity_OrthoParams.w > 0.5) {// ortho
					posVS = float4(temp.xy, depthES, 1);
				}
				else {
					posVS = float4(depthES * temp.xyz, 1);
				}
				return mul(_ProPixelizer_LowRes_I_V, posVS).xyz;
			}

#if NORMAL_EDGE_DETECTION_ON

			static const float ORTHO_NORMAL_PERP_COMPONENT_THRESHOLD_DELTA = 0.2;
			static const float ORTHO_DEPTH_DELTA_THRESHOLD_SCENE_UNITS = 0.25;
			static const float THRESHOLD_LOCAL_DEPTH_CURVATURE_FRACTION = 0.05;
			static const float THRESHOLD_DISTANCE_FOR_FAR_NEIGHBOUR_EDGE = 0.5;
			static const float THRESHOLD_DISTANCE_FOR_NEARER_NEIGHBOUR_OCCLUSION = 1;

			/// <summary>
			/// Calculates if there is an edge present due to normal folds.
			///
			/// us: pixel being rendered
			/// nbrA, nbrB: pair of opposing neighbour pixels
			/// </summary>
			inline float isNotEdge(Pixel us, Pixel nbrA, Pixel nbrB, inout float3 average) {
				float3 favoredDirection = float3(0.1, 0.1, -0.99);
				// convert vectors to world space and then dot with favored dir.
				float nbrAF = dot(nbrA.normalWS, favoredDirection);
				float usF = dot(us.normalWS, favoredDirection);
				float weAreFavored = step(nbrAF, usF);
				float sameIDasA = nbrA.ID - us.ID < 0.001;
				float nbrAPixelated = step(0.5, nbrA.pixelSize); // 1 if pixelSize > 0.5

				// perform the comparison
				float similarity = dot(normalize(us.normalVS), normalize(nbrA.normalVS));
				float comparison = step(similarity, (1 / _NormalEdgeDetectionSensitivity));

				float farInFrontOfNeighbour = 0;
				float farBehindNeighbour = 0;
				#if DEPTH_TEST_NORMAL_EDGES_ON
					float3 usWS = us.positionWS;
					float actualDepthDelta = nbrA.depthVS - us.depthVS;

					// note that the depth comparisons are in scene units!
					if (unity_OrthoParams.w > 0.5) { // ortho
						// world space position of neighbour, if it were at the same depth as us.
						float3 flatNbrWS = getWorldSpacePosition(nbrA.texel, us.depth);
						float nbrADeltaWSLength = length(flatNbrWS - usWS);
						float3 nbrADeltaNorm = float3(normalize(nbrA.deltaCS), 0);
						float normalPerpComponent = dot(nbrADeltaNorm, nbrA.normalVS); // sign tells us if we expect neighbour to be nearer or further away.
						// Estimate a range of possible Depth Deltas that we could expect.
						float npcMin = normalPerpComponent - ORTHO_NORMAL_PERP_COMPONENT_THRESHOLD_DELTA;
						float npcMax = normalPerpComponent + ORTHO_NORMAL_PERP_COMPONENT_THRESHOLD_DELTA;
						float expectedDDA = nbrADeltaWSLength * npcMin / -nbrA.normalVS.z;
						float expectedDDB = nbrADeltaWSLength * npcMax / -nbrA.normalVS.z;

						if (actualDepthDelta < 0 && actualDepthDelta < min(min(expectedDDA, expectedDDB), -ORTHO_DEPTH_DELTA_THRESHOLD_SCENE_UNITS))
							farInFrontOfNeighbour = 1;
						if (actualDepthDelta > 0 && actualDepthDelta > max(3.0*max(expectedDDA, expectedDDB), ORTHO_DEPTH_DELTA_THRESHOLD_SCENE_UNITS))
							farBehindNeighbour = 1;
					}
					else { // perspective
						// Use neighbour B to predict the depth of neighbour A, comparison in raw depth space.
						bool bothNeighboursPixelated = (nbrA.pixelSize > 0.5 && nbrB.pixelSize > 0.5);
						bool sameIDasB = abs(nbrB.ID - us.ID) < 0.001;
						bool nbrBWithinTextureBounds = all(nbrB.texel >= 0.0) && all(nbrB.texel <= 1.0);
						bool valid = bothNeighboursPixelated && sameIDasB && nbrBWithinTextureBounds;
						float depthCurvature = abs(nbrA.depth + nbrB.depth - 2.0 * us.depth);
						float depthPrecisionTolerance = max(1.5e-7, 4e-7 * max(us.depth, max(nbrA.depth, nbrB.depth))); // manually tuned
						float localDepthSlope = abs(us.depth - nbrB.depth);
						float depthTolerance = max(depthPrecisionTolerance, THRESHOLD_LOCAL_DEPTH_CURVATURE_FRACTION * localDepthSlope);
						bool depthDiscontinuityDetected = valid && depthCurvature > depthTolerance;

						if (depthDiscontinuityDetected && actualDepthDelta > THRESHOLD_DISTANCE_FOR_FAR_NEIGHBOUR_EDGE)
							farInFrontOfNeighbour = 1;
						if (depthDiscontinuityDetected && actualDepthDelta < -THRESHOLD_DISTANCE_FOR_NEARER_NEIGHBOUR_OCCLUSION)
							farBehindNeighbour = 1;
					}
				#endif

				float edge = (((comparison > 0.5 && weAreFavored) || (!nbrAPixelated) || (farInFrontOfNeighbour)) && !farBehindNeighbour);

				// peel normals backwards if neighbour is neither us, pixelated, or a depth-identified normal edge.
				float3 nbrANormal = lerp(nbrA.normalVS, float3(0, 0, -0.3), (1 - nbrAPixelated * sameIDasA * !farInFrontOfNeighbour));

				// If behind neighbour, don't include neighbour in average - we are probably occluded.
				nbrANormal = lerp(nbrANormal, us.normalVS, farBehindNeighbour);
				average = lerp(average, us.normalVS + nbrANormal, edge);

				return 1 - edge;
			}
#endif

			void frag(ProPVaryings i, out float4 color: COLOR) {
				float2 mainTexel = i.scrPos.xy;
				float4 packed = SAMPLE_TEXTURE2D(_BlitTexture, sampler_BlitTexture_point_clamp, mainTexel);
				Pixel us = buildPixel(mainTexel, float2(0, 0), packed);
				#if DEPTH_TEST_NORMAL_EDGES_ON
					if (unity_OrthoParams.w > 0.5)
						us.positionWS = getWorldSpacePosition(us.texel, us.depth);
				#endif

				// if this pixel is not pixelised, return no edge.
				if (us.pixelSize < 1)
				{
					color = float4(0,0,0,0);
					return;
				}
				
				Pixel neighbours[9];
				neighbours[0] = samplePixel(mainTexel, float2(-1, 1), us.pixelSize);
				neighbours[1] = samplePixel(mainTexel, float2( 0, 1), us.pixelSize);
				neighbours[2] = samplePixel(mainTexel, float2( 1, 1), us.pixelSize);
				neighbours[3] = samplePixel(mainTexel, float2(-1, 0), us.pixelSize);
				neighbours[4] = us;
				neighbours[5] = samplePixel(mainTexel, float2( 1, 0), us.pixelSize);
				neighbours[6] = samplePixel(mainTexel, float2(-1,-1), us.pixelSize);
				neighbours[7] = samplePixel(mainTexel, float2( 0,-1), us.pixelSize);
				neighbours[8] = samplePixel(mainTexel, float2( 1,-1), us.pixelSize);

				float countSimilar = 0;
				[unroll]
				for (int i = 0; i < 9; i++) {
					countSimilar += compareIDs(us, neighbours[i]);
				}

				float IDfactor = countSimilar > 7 ? 0.0 : 1.0;

				// Edge detection through normals.
				#if NORMAL_EDGE_DETECTION_ON
					float notEdge = 1;
					float3 average = us.normalVS;
					notEdge *= isNotEdge(us, neighbours[1], neighbours[7], average); // up
					notEdge *= isNotEdge(us, neighbours[5], neighbours[3], average); // right
					notEdge *= isNotEdge(us, neighbours[7], neighbours[1], average); // down
					notEdge *= isNotEdge(us, neighbours[3], neighbours[5], average); // left
					average = normalize(average);
					float edgeFactor = 1 - notEdge;
				#else
					float edgeFactor = 0;
					float3 average = float3(0, 0, 1);
				#endif
				color = PackOutline(IDfactor, edgeFactor, average);
			}
		
		ENDHLSL
		}
	}
}
