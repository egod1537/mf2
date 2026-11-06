# ProPixelizer Subtarget for ShaderGraph

## What is the ProPixelizer Subtarget?

ShaderGraph is a graph-based shader editor, which allows you to create shaders for Unity by assembling graphs from nodes.
Since version 1.0 of ProPixelizer, I've used ShaderGraph to define the look and feel of the ProPixelizer shader, including
cel-shading, lighting, and dither patterns. The intention behind this has always been to make ProPixelizer as easily modifiable
as possible, so that people can tweak it to get whatever look they like.

However, ShaderGraph has a fairly big limitation - it is incapable of adding Custom Passes. This has been an often requested
feature, see for example this forum post:
https://forum.unity.com/threads/how-to-add-a-pass-tag-to-a-shadergraph.865594/

A custom pass is required to generate ID-based outline information in ProPixelizer. In previous versions of ProPixelizer, I
added this pass to the ShaderGraph shader by creating a second shader, PixelizedWithOutline, and used UsePass to combine
the passes from the ShaderGraph shader with the ProPixelizer pass. It works, but is messy, and made editing the graphs a nuisance.

Starting from v2.0 of ProPixelizer, I have added a way for advanced users to directly create all required passes in a single ShaderGraph shader.
This works by creating a 'SubTarget' for shadergraph. No further installation is required, if you have added ProPixelizer to your project
you should find the ProPixelizer subtarget is available as a dropdown in the shadergraph master node.

For further information, please see the examples in "ProPixelizer/Example Assets/ShaderGraph".