// Copyright Elliot Bentine, 2018-
// 
// Produces a texture used to map screen pixels to their pixelated location.
Shader "Hidden/ProPixelizer/SRP/Pixelization Map" {
	Properties{
		_MainTex("Texture", any) = "" {}
		_SourceDepthTexture("Texture", any) = "" {}
		_SceneDepthTexture("Texture", any) = "" {}
	}

	SubShader{
	Tags{
		"RenderType" = "Opaque"
		"PreviewType" = "Plane"
		"RenderPipeline" = "UniversalPipeline"
	}

	Pass{
		Cull Off
		ZWrite Off
		ZTest Off
		Blend Off

		HLSLPROGRAM 
		#pragma vertex vert
		#pragma fragment frag
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "PixelUtils.hlsl"
		#include "PackingUtils.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"		
		#pragma multi_compile ORTHO_PROJECTION _
		#pragma multi_compile OVERLAY_CAMERA

		uniform sampler2D _MainTex;
		float4 _MainTex_TexelSize;

		// Depth texture used for pixelization map source.
		TEXTURE2D_X_FLOAT(_SourceDepthTexture);
		SAMPLER(sampler_SourceDepthTexture_point_clamp);

		struct ProPVaryings {
			float4 pos : SV_POSITION;
			float2 scrPos : TEXCOORD1;
		};

		// 2022.2 & URP14+
		#define BLIT_API UNITY_VERSION >= 202220
		#if BLIT_API
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

		ProPVaryings vert(Attributes v) {
				Varyings vars;
				vars = Vert(v);
				ProPVaryings o;
				//o.pos = TransformObjectToHClip(v.vertex.xyz);
				o.pos = vars.positionCS;
				o.scrPos = vars.texcoord;
				return o;
			}
		#else
			struct Attributes
			{
				float4 vertex   : POSITION;  // The vertex position in model space.
				float3 normal   : NORMAL;    // The vertex normal in model space.
				float4 texcoord : TEXCOORD0; // The first UV coordinate.
			};

			ProPVaryings vert(Attributes v) {
				ProPVaryings o;
				o.pos = TransformObjectToHClip(v.vertex.xyz);
				o.scrPos = ComputeNormalizedDeviceCoordinatesWithZ(o.pos.xyz).xy;
				return o;
			}
		#endif

		void frag(ProPVaryings i, out float4 pixelMap: COLOR) {

			float depth, nearestDepth, nearestRawDepth, nearestPixelSize;
			float2 ppos;
			float raw_depth;
			float2 screenUV = (floor(i.scrPos * _MainTex_TexelSize.zw) + 0.5) * rcp(_MainTex_TexelSize.zw); // screen uv is calculated and snapped to a pixel centre.

			// shift of one pixel
			float2 pShift = float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y);

			nearestDepth = 1;
			#ifdef OVERLAY_CAMERA
				nearestRawDepth = SAMPLE_TEXTURE2D_X(_SourceDepthTexture, sampler_SourceDepthTexture_point_clamp, UnityStereoTransformScreenSpaceTex(screenUV)).r;
			#else
				nearestRawDepth = SampleSceneDepth(screenUV);
			#endif
			float2 nearestScreenUV = screenUV;
			float pixelSizeEncoded = 0;
			nearestPixelSize = 0;

			[unroll]
			for (int u = -2; u <= 2; u++)
			{
				[unroll]
				for (int v = -2; v <= 2; v++)
				{
					//Get coord of neighbouring pixel for sampling
					float shiftx = u * pShift.x;
					float shifty = v * pShift.y;
					float2 ppos = screenUV + float2(shiftx, shifty);
					float4 neighbour = tex2D(_MainTex, ppos);
					pixelSizeEncoded = neighbour.a;
					float pixelSize = AlphaToPixelSize(pixelSizeEncoded);
					float pos = floor(pixelSize / 1.99);
					float neg = -floor((pixelSize - 1) / 1.99);
					bool pixelate = pixelSize > 0.5 && u >= neg && v >= neg && u <= pos && v <= pos;
					raw_depth = SAMPLE_TEXTURE2D_X(_SourceDepthTexture, sampler_SourceDepthTexture_point_clamp, UnityStereoTransformScreenSpaceTex(ppos)).r;
#ifdef ORTHO_PROJECTION
	#if UNITY_REVERSED_Z
					depth = -raw_depth;
	#else
					depth = raw_depth;
	#endif
#else
					depth = Linear01Depth(raw_depth.r, _ZBufferParams);
#endif
					bool nearer = (depth < nearestDepth);
					nearestDepth = nearer && pixelate ? depth : nearestDepth;
					nearestRawDepth = nearer && pixelate ? raw_depth : nearestRawDepth;
					nearestScreenUV = nearer && pixelate ? ppos : nearestScreenUV;
					nearestPixelSize = nearer && pixelate ? pixelSizeEncoded : nearestPixelSize;
				}

				// Store the shifted uv coordinates in the pixelisation map.
				pixelMap = PackPixelMap(screenUV, nearestScreenUV, _MainTex_TexelSize, nearestPixelSize);
			}
		}
		ENDHLSL
	}
	}
}
