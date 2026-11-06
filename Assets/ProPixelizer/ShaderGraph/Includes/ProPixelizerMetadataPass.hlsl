// Copyright Elliot Bentine, 2018-
#ifndef PROPIXELIZER_METADATA_PASS_INCLUDED
#define PROPIXELIZER_METADATA_PASS_INCLUDED

// Shader passes used for metadata in ProPixelizer's ShaderGraph target.

PackedVaryings vert(Attributes input)
{
    Varyings output = (Varyings)0;
    output = BuildVaryings(input);
    PackedVaryings packedOutput = (PackedVaryings)0;
    packedOutput = PackVaryings(output);
    return packedOutput;
}

float4 frag(PackedVaryings packedInput) : SV_TARGET
{
    #if !defined(PROPIXELIZER_SUBTARGET)
    // Properties won't exist in shadergraph preview.
    float _ID = 1;
    float _PixelSize = 1;
    float _ProPixelizer_Pixel_Scale = 1;
    #endif

    Varyings unpacked = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(unpacked);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(unpacked);
    clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);

    float4 color;
    float3 normalVS = TransformWorldToViewDir(unpacked.normalWS); 
    PackMetadata(_ID, max(1, round(_PixelSize* _ProPixelizer_Pixel_Scale)), normalVS, color);
    float3 normalCS = TransformWorldToViewDir(unpacked.normalWS);
    #if NORMAL_EDGE_DETECTION_ON
        color.rb = normalCS.rg * 0.5 + 0.5;
    #endif
    return color;
}

#endif