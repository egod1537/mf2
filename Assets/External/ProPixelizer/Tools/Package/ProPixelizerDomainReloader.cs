// Copyright Elliot Bentine, 2018-
#if UNITY_EDITOR
using UnityEditor;

namespace ProPixelizer
{
    /// <summary>
    /// This fixes an issue with Unity importing the shaders in the wrong order.
    /// 
    /// PixelizedWithOutline requires the forward rendering passes from ProPixelizerBase.
    /// Sometimes Unity imports them in the wrong order, and then ProPixelizer materials appear
    /// invisible.
    /// 
    /// The 'fix' is to reimport them in the correct order.
    /// 
    /// https://forum.unity.com/threads/srpbatcher-ignores-usepass-for-shaderlab-shaders-2021-3-11f1.1352660/
    /// </summary>
    public class ProPixelizerDomainReloader : AssetPostprocessor
    {
        #if UNITY_2021_2_OR_NEWER
        //static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        //{
        //    if (didDomainReload)
        //    {
        //          ProPixelizerVerification.ReimportShaders();
        //    }
        //}
        #endif
    }
}
#endif
