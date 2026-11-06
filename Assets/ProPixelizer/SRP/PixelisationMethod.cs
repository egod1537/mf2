// Copyright Elliot Bentine, 2018-
namespace ProPixelizer
{
    // Original,
    // Just low res render target, after transparents
    // Low res render target, scene minus characters (original shader)
    // 

    // I may want to render the whole screen to low res, including ProPixelizer stuff
    // I may want to just render ProPixelizer materials, or a layer to, to low res.
    // I may want to render just transparents low res


    // Low Res: After Opaques, After Transparents
    // Filtering: LayerMask, ProPixelizer materials only


    // Notes:
    // * What is the best way to force the main draw ordering to use low res? I want to minimize the number of duplicate draw calls.
    // * I don't want to draw the scene twice at low res, or at all at high res if only low res is desired.
    // * SRPs don't really have a way to remove a render pass at the moment! I could introduce a camera overriding pass at the start
    //   that intercepts the start of the SRP draw and manipulates the target info. Seems a bit hacky...
    // * Entire scene seems a special case.

    public enum PixelisationMethod
    {
        DitherExpansion,
        LowResRenderTarget
    }

    // Modify renderingData in OnCameraSetup to:
    // * Change filtering options, so that pixelized objects are _not_ drawn by the scene camera.
    // * Change resolution of camera, when pixelating everything.

    public enum PixelisationModel
    {
        PixelizeEverything
    }


    // If per-object pixelisation is disabled, Render Feature should set Global float property PixelSize to 0 so that it affects everything.

    // Ways to hide objects from non-pixelized scene
    // * Cull pixels if some global const says the main camera is drawing. Wasteful.
    // * Handling culling at object level, e.g. via layers or exclusion of shader tags (better).
    // * (Possibly both - so that it works out of the box for users who can't understand the docs).


    // At the very start of scene rendering, in BeginCameraRendering:
    // - the size of the low res target is chosen, according to either
    //   * fixed height (aspect ratio follows screen) + border
    //   * fixed width (aspect ratio follows screen)  + border
    //   * for ortho camera, size derived from world-space pixel size + border
    //   * relative size + border

    // Note that size of ortho view will need to be snapped to draw size values corresponding to integer sized targets.



    // Camera Snap development notes:
    // - snap low-res camera such that WS 0,0,0 is aligned to low-res camera target pixel centre.
    // - then _only need snapable on objects that are moving_, and stationary objects will keep same pixel positions
    // - store offset position of low-res camera in screen pixels
    // -> use offset when recompositing images
    // 
    // - provide a layer mask option, to include other objects on the low-res target
    // - how to combine transparents to low-res target?
    // - render to low-res target
}