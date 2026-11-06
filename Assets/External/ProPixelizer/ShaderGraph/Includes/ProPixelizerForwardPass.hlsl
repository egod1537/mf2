// Parts copyright Elliot Bentine 2024-

SamplerState lightingRamp_point_clamp_sampler;

/// <summary>
/// Applies the cel shading lighting ramp to the given input light color and returns the new light color.
/// </summary>
void applyLightingRamp(float4 inputLight, out float4 output, out float shadow_tint_strength) {
    // RgbToHsv: defined in Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl
    float3 hsv = RgbToHsv(inputLight.rgb);
    float normalised_brightness = hsv.b / _LightingRampRange;
    float sat_normalised_brightness = saturate(normalised_brightness);
    hsv.b = SAMPLE_TEXTURE2D(_LightingRamp, lightingRamp_point_clamp_sampler, float2(sat_normalised_brightness, 0.5)).b;
    output.rgb = HsvToRgb(hsv);
    output.a = inputLight.a;
    shadow_tint_strength = saturate(pow(1.1 - sat_normalised_brightness, 2.5 - 2 * _ShadowTintColor.a)) * _ShadowTintColor.a;
}

SAMPLER(sampler_lut_point_clamp);

void getProPixelizerOutlines(float4 screenPos, out float outlineFactor, out float edgeFactor, out float3 edgeNormalVS) {
    // screenPos is equivalent to SurfaceDescriptionInputs.ScreenPosition, which is a transformed Varyings.screenPosition.
    float2 screenCoord = screenPos.rg / screenPos.a;
    GetOutline_float(screenCoord.rg, outlineFactor, edgeFactor, edgeNormalVS);
}

void ProPixelizer_frag(
    PackedVaryings packedInput
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
)
{
    outColor = float4(0.5, 0.5, 0.0, 1.0);

    // unpack surface from varyings
    Varyings unpacked = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(unpacked);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(unpacked);
    // Unfortunately have to double tap, but think this is worth being more future-proof.
    SurfaceDescriptionInputs surfaceDescriptionInputs = BuildSurfaceDescriptionInputs(unpacked);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(unpacked);
    // Issue #240:
    // - Remove scene lighting effect via Ambient occlusion when ambient light is set to user controlled (alpha = 1)
    surfaceDescription.Occlusion = lerp(surfaceDescription.Occlusion, 0, saturate(_AmbientLight.a));

    bool isTransparent = false;
    half alpha = AlphaDiscard(surfaceDescription.Alpha, surfaceDescription.AlphaClipThreshold);

    // ignore LOD cross fade

    // Setup surface for lighting calculation
    InputData inputData;
    InitializeInputData(unpacked, surfaceDescription, inputData);
    if (_ProPixelizer_VirtualCameraLowResDraw > 0.5)
    {
        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(
            ClampScreenPixelPosToScreenTarget(
                TransformLowResPixelPosToScreenPixelPos(unpacked.positionCS)
            )
        );
    }
    //#ifdef VARYINGS_NEED_TEXCOORD0
    //    SETUP_DEBUG_TEXTURE_DATA(inputData, unpacked.texCoord0);
    //#else
    //    SETUP_DEBUG_TEXTURE_DATA_NO_UV(inputData);
    //#endif
    #if UNITY_VERSION >= 60000000 
        InitializeBakedGIData(unpacked, inputData);
    #endif

#ifdef _SPECULAR_SETUP
    float3 specular = surfaceDescription.Specular;
    float metallic = 1;
#else
    float3 specular = 0;
    float metallic = surfaceDescription.Metallic;
#endif

    half3 normalTS = half3(0, 0, 0);
#if defined(_NORMALMAP) && defined(_NORMAL_DROPOFF_TS)
    normalTS = surfaceDescription.NormalTS;
#endif

    SurfaceData surface;
    surface.albedo = surfaceDescription.BaseColor;
    surface.metallic = saturate(metallic);
    surface.specular = specular;
    surface.smoothness = saturate(surfaceDescription.Smoothness),
    surface.occlusion = surfaceDescription.Occlusion,
    surface.emission = surfaceDescription.Emission,
    surface.alpha = saturate(alpha);
    surface.normalTS = normalTS;
    surface.clearCoatMask = 0;
    surface.clearCoatSmoothness = 1;

    // We want to apply the lighting ramp _only_ to the light values, not to base * light values.
    // To achieve this, we calculate PBR lighting for a dummy surface with white albedo value and no emissive. 
    surface.albedo = float3(1.0, 1.0, 1.0);
    surface.emission = float3(0.0, 0.0, 0.0);
    
    float3 unlitColor = surfaceDescription.BaseColor;
    #if defined(_DBUFFER) 
        ApplyDecalToBaseColorAndNormal(
            ClampScreenPixelPosToScreenTarget(
                TransformLowResPixelPosToScreenPixelPos(unpacked.positionCS)
            ),
            unlitColor, // Note we still want lighting calculated for cel shading using a white color; we then post multiply later with the decal/albedo.
            inputData.normalWS
        );
    #endif

    // Get ProPixelizer outline factors and normals
    float4 screenPos = surfaceDescriptionInputs.ScreenPosition;
    float ProPixelizer_outlineFactor;
    float ProPixelizer_edgeFactor;
    float3 ProPixelizer_edgeNormalVS;
    getProPixelizerOutlines(screenPos, ProPixelizer_outlineFactor, ProPixelizer_edgeFactor, ProPixelizer_edgeNormalVS);

    // Surface normal lerps to edgeNormalVS (transformed to TS) by edge factor and bevel weight.
    float bevelFactor = saturate(ProPixelizer_edgeFactor * surfaceDescription.ProPixelizerBevelWeight);
    float3 edgeNormalWS = TransformViewToWorldDir(ProPixelizer_edgeNormalVS);
    float3x3 tangentTransform = float3x3(surfaceDescriptionInputs.WorldSpaceTangent, surfaceDescriptionInputs.WorldSpaceBiTangent, surfaceDescriptionInputs.WorldSpaceNormal);
    float3 edgeNormalTS = TransformWorldToTangent(edgeNormalWS, tangentTransform, true);
    inputData.normalWS = normalize(lerp(inputData.normalWS, edgeNormalWS, bevelFactor.xxx));
    float4 preRampLight = UniversalFragmentPBR(inputData, surface);

    // Ambient light weight
    preRampLight = preRampLight + lerp(float4(0, 0, 0, 1), float4(_AmbientLight.rgb, 1), _AmbientLight.a);

    // Perform light calculation

    // Mix light channels according to pre-grading weights

    // Apply lighting ramp to light values, leave values >1 as is for bloom.
    float4 postRampLight;
    float shadow_tint_strength;
    applyLightingRamp(preRampLight, postRampLight, shadow_tint_strength);

    // Apply ramped light to albedo, add emissive. If lit color exceeds 1, store the excess color.
    //float3 litColor = surfaceDescription.BaseColor * postRampLight.rgb + surfaceDescription.Emission;
    //float maxLitColorComponent = max(max(litColor.r, litColor.g), litColor.b);
    //float3 lightingOverSaturation = max(0, maxLitColorComponent - 1) * litColor;

    // Alternative: Albedo is ramped, emissive is used to tint final.
    float3 litColor = unlitColor * postRampLight.rgb;
    float3 lightingOverSaturation = surfaceDescription.Emission;

    // Shadow tint:
    litColor.rgb = lerp(litColor.rgb, _ShadowTintColor.rgb, shadow_tint_strength);

    // LINES --------------

    // Apply edges
    // - EdgeHighlightColor multiplied by two, then multiplied by 
    float edgeFactor = ProPixelizer_edgeFactor * surfaceDescription.ProPixelizerEdgeHighlight.a;
    litColor.rgb = lerp(litColor, litColor * 2.0 * surfaceDescription.ProPixelizerEdgeHighlight.rgb, edgeFactor);

    // Apply outline
    float outlineFactor = ProPixelizer_outlineFactor * surfaceDescription.ProPixelizerOutlineColor.a;
    litColor.rgb = lerp(litColor, surfaceDescription.ProPixelizerOutlineColor.rgb, outlineFactor);

    // Apply Color Grading
#if COLOR_GRADING_ON
    float3 ObjectCentre_WS;
    float PixelSize;
    ProPixelizerGridSettings_float(SHADERGRAPH_OBJECT_POSITION, ObjectCentre_WS, PixelSize);
    float4 posCS = float4(unpacked.positionCS.xy, 0.0, 0.0);
    float4 screenParams;
    GetScaledScreenParameters_float(screenParams);
    float alpha_out;
    float2 ditherUV;
    PixelClipAlpha_float(UNITY_MATRIX_VP, ObjectCentre_WS, screenParams, posCS, PixelSize, surfaceDescription.Alpha, surfaceDescription.AlphaClipThreshold, alpha_out, ditherUV);
    float4 gradedColor;
    InternalColorGrade(_PaletteLUT, sampler_lut_point_clamp, float4(litColor.rgb, 1), ditherUV, gradedColor);
#else
    float4 gradedColor = float4(litColor.rgb, 1);
#endif

    // Apply post-grading lighting
    litColor = gradedColor.rgb + lightingOverSaturation.rgb * gradedColor.rgb;

    outColor = float4(litColor, 1);
    outColor.rgb = MixFog(outColor.rgb, inputData.fogCoord);
    outColor.a = OutputAlpha(outColor.a, false);

    // Debugging:
    // outColor = float4(ProPixelizer_edgeNormalVS.rgb, 1);
    // outColor.xy = TransformLowResPixelPosToScreenPixelPos(unpacked.positionCS) / 1000;


    #ifdef _WRITE_RENDERING_LAYERS
    uint renderingLayers = GetMeshRenderingLayer();
    outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
    #endif
}

//
///// <summary>
///// Calculates lighting data for a given surface.
///// </summary>
//LightingData CalculateLightingData(InputData inputData, SurfaceData surfaceData) {
//    BRDFData brdfData;
//    InitializeBRDFData(surfaceData, brdfData);
//
//#if defined(DEBUG_DISPLAY)
//    half4 debugColor;
//
//    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
//    {
//        return debugColor;
//    }
//#endif
//
//    // Clear-coat calculation...
//    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
//    half4 shadowMask = CalculateShadowMask(inputData);
//    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
//    uint meshRenderingLayers = GetMeshRenderingLayer();
//    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
//
//    // NOTE: We don't apply AO to the GI here because it's done in the lighting calculation below...
//    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
//
//    LightingData lightingData = CreateLightingData(inputData, surfaceData);
//
//    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
//        inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
//        inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
//#ifdef _LIGHT_LAYERS
//    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
//#endif
//    {
//        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat,
//            mainLight,
//            inputData.normalWS, inputData.viewDirectionWS,
//            surfaceData.clearCoatMask, specularHighlightsOff);
//    }
//
//#if defined(_ADDITIONAL_LIGHTS)
//    uint pixelLightCount = GetAdditionalLightsCount();
//
//#if USE_FORWARD_PLUS
//    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
//    {
//        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
//
//            Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
//
//#ifdef _LIGHT_LAYERS
//        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
//#endif
//        {
//            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
//                inputData.normalWS, inputData.viewDirectionWS,
//                surfaceData.clearCoatMask, specularHighlightsOff);
//        }
//    }
//#endif
//
//    LIGHT_LOOP_BEGIN(pixelLightCount)
//        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
//
//#ifdef _LIGHT_LAYERS
//    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
//#endif
//    {
//        lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
//            inputData.normalWS, inputData.viewDirectionWS,
//            surfaceData.clearCoatMask, specularHighlightsOff);
//    }
//    LIGHT_LOOP_END
//#endif
//
//#if defined(_ADDITIONAL_LIGHTS_VERTEX)
//        lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
//#endif
//}
