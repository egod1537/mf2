// Copyright Elliot Bentine 2018-
//
// See ProPixelizerSubtarget:
//   ProPixelizer inserts a wrapper around `BuildSurfaceDescription`. To do this, we undefine, alias, then redefine this symbol.
//   This allows Unity's passes to use our wrapper automatically. Implementing the wrapper requires us to sandwich the Unity definition
//   with the ProPixelizerSurfaceDescriptionPre and Post.

#ifndef PROPIXELIZER_SURFACE_DESCRIPTION_POST_INCLUDED
#define PROPIXELIZER_SURFACE_DESCRIPTION_POST_INCLUDED

#undef BuildSurfaceDescription

SurfaceDescription BuildSurfaceDescription(Varyings varyings)
{
    // This pretty much re-implements the old subgraph that had to be added during most of the ProPixelizer v2.0 beta.
    // It modifies the alpha to produce a dither pattern required for pixel expansion; because this dither pattern is exactly overlaid
    // with the (old) subgraph implementation, if you have both (ie, haven't updated your custom shadergraphs) it will still work,
    // because the two patterns sit on top of each other and both are therefore visible.

    SurfaceDescription surfaceDescription = ProPixelizerBuildSurfaceDescriptionRaw(varyings);

    float3 objectCentreWS;
    float pixelSize;
    ProPixelizerGridSettings_float(
        SHADERGRAPH_OBJECT_POSITION,
        objectCentreWS,
        pixelSize
    );
    float4 pixelPositionCS = float4(varyings.positionCS.xy, 0.0, 0.0);

    float4 screenParameters;
    GetScaledScreenParameters_float(screenParameters);

    float pixelatedAlpha;
    float2 unusedDitherUV;
    PixelClipAlpha_float(
        UNITY_MATRIX_VP,
        objectCentreWS,
        screenParameters,
        pixelPositionCS,
        pixelSize,
        surfaceDescription.Alpha,
        surfaceDescription.AlphaClipThreshold,
        pixelatedAlpha,
        unusedDitherUV
    );

    surfaceDescription.Alpha = pixelatedAlpha;
    return surfaceDescription;
}

#endif
