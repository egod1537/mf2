// Copyright Elliot Bentine 2018-
//
// See ProPixelizerSubtarget:
//   ProPixelizer inserts a wrapper around `BuildSurfaceDescription`. To do this, we undefine, alias, then redefine this symbol.
//   This allows Unity's passes to use our wrapper automatically. Implementing the wrapper requires us to sandwich the Unity definition
//   with the ProPixelizerSurfaceDescriptionPre and Post.

#ifndef PROPIXELIZER_SURFACE_DESCRIPTION_PRE_INCLUDED
#define PROPIXELIZER_SURFACE_DESCRIPTION_PRE_INCLUDED

// Rename URP's generated function so that the postgraph include can wrap it.
#define BuildSurfaceDescription ProPixelizerBuildSurfaceDescriptionRaw

#endif
