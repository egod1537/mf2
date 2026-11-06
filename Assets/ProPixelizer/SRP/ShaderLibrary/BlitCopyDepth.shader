// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)
// Copies depth from depth target

Shader "Hidden/ProPixelizer/SRP/BlitCopyDepth" {
	Properties{
		_BlitTexture("Texture", any) = "" {}
		[HideInInspector] _ProPixelizer_CopyDepth_ZWrite("ZWrite", Float) = 1
		[HideInInspector] _ProPixelizer_CopyDepth_ColorMask("Color Mask", Float) = 0
	}
		SubShader{
			Pass {
				ZTest Off Cull Off
				ZWrite [_ProPixelizer_CopyDepth_ZWrite]
				ColorMask [_ProPixelizer_CopyDepth_ColorMask]

				HLSLPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 2.0
				#pragma multi_compile_local_fragment _ PROPIXELIZER_COPY_DEPTH_OUTPUT_COLOR

				// 2022.2 & URP14+
				#define BLIT_API UNITY_VERSION >= 202220
				#if BLIT_API
					#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
					#define USE_FULL_PRECISION_BLIT_TEXTURE // NB: For Unity 6, does nothing on 2022.3
					#include "Patch2022_UseFullSamplerPrecision.hlsl" // ... and as above but for 2022.3
					#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
					#include "Patch2022_RestoreSamplerPrecision.hlsl"
					#undef USE_FULL_PRECISION_BLIT_TEXTURE

					SAMPLER(sampler_Depth_point_clamp);

					struct BCMTADVaryings {
						float4 vertex : SV_POSITION;
						float2 texcoord : TEXCOORD0;
						UNITY_VERTEX_OUTPUT_STEREO
					};

					BCMTADVaryings vert(Attributes v) {
						Varyings vars;
						vars = Vert(v);
						BCMTADVaryings o;
						o.vertex = vars.positionCS;
						o.texcoord = vars.texcoord;
						return o;
					}
				#else
					#include "UnityCG.cginc"

					UNITY_DECLARE_DEPTH_TEXTURE(_BlitTexture);
					uniform float4 _BlitTexture_ST;

					struct BCMTADVaryings {
						float4 vertex : SV_POSITION;
						float2 texcoord : TEXCOORD0;
						UNITY_VERTEX_OUTPUT_STEREO
					};

					struct Attributes
					{
						float4 vertex : POSITION;
						float2 texcoord : TEXCOORD0;
						UNITY_VERTEX_INPUT_INSTANCE_ID
					};

					BCMTADVaryings vert(Attributes v) {
						BCMTADVaryings o;
						UNITY_SETUP_INSTANCE_ID(v);
						UNITY_INITIALIZE_OUTPUT(BCMTADVaryings, o);
						UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
						o.vertex = UnityObjectToClipPos(v.vertex);
						o.texcoord = TRANSFORM_TEX(v.texcoord.xy, _Depth);
						return o;
					}
				#endif

				#if defined(PROPIXELIZER_COPY_DEPTH_OUTPUT_COLOR)
					float frag(BCMTADVaryings i) : SV_Target
				#else
					float frag(BCMTADVaryings i) : SV_Depth
				#endif
				{
					#if BLIT_API
						return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_Depth_point_clamp, i.texcoord).r;
					#else
						return SAMPLE_RAW_DEPTH_TEXTURE(_BlitTexture, i.texcoord);
					#endif
				}
			ENDHLSL
		}
	}
	Fallback "Hidden/Universal Render Pipeline/Blit"
}
