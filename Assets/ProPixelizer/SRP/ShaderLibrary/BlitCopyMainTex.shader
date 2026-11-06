// Unity built-in shader source. Copyright (c) 2016 Unity Technologies. MIT license (see license.txt)
// Modified for ProPixelizer (for Blit API compatibility across versions).

Shader "Hidden/ProPixelizer/SRP/BlitCopyMainTex" {
	Properties{ _MainTex("Texture", any) = "" {} }
		SubShader{
			Pass {
				ZTest Always Cull Off ZWrite On

				HLSLPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma target 2.0

				// 2022.2 & URP14+
				#define BLIT_API UNITY_VERSION >= 202220
				#if BLIT_API
					#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
					#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

					sampler2D _MainTex;

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
					sampler2D _MainTex;

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
						o.texcoord = v.texcoord.xy;
						return o;
					}
				#endif

				float4 frag(BCMTADVaryings i) : SV_Target
				{
					#if UNITY_UV_STARTS_AT_TOP
						//i.texcoord.y = 1 - i.texcoord.y;
					#endif
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
					return tex2D(_MainTex, i.texcoord);
				}
				ENDHLSL

			}
		}
	Fallback "Hidden/Universal Render Pipeline/Blit"
}