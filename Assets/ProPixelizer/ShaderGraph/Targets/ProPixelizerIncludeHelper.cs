#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace ProPixelizer.Universal.ShaderGraph
{
    /// <summary>
    /// Unique .meta GUIDs for each hlsl file included by the ProPixelizer subtarget.
    /// </summary>
    public static class ProPixelizerIncludedShaderGUIDS
    {
        public enum IncludedShaderFile
        {
            MetadataPass,
            ProPixelizerForwardPass,
            PixelUtils,
            PackingUtils,
            OutlineUtils,
            ScreenUtils,
            ColorGrading,
            SurfaceDescriptionPre,
            SurfaceDescriptionPost,
            SGSubTargetHelpers,
            ShaderGraphUtils
        }

        private static string GetGuid(IncludedShaderFile shader)
        {
            switch(shader)
            {
                case IncludedShaderFile.MetadataPass: return "d65c8d7dbe257564e92fcf945c7a002a";
                case IncludedShaderFile.ProPixelizerForwardPass: return "1b4124f8dfc86ff4d8efe7f6d375d7db";
                case IncludedShaderFile.PixelUtils: return "0188cfc2d87c7dd40a2d6ffb22813fce";
                case IncludedShaderFile.PackingUtils: return "d1b79a7d5c7f28646bd6408ec2b1d604";
                case IncludedShaderFile.OutlineUtils: return "8c3b969a8d2f528479b4473c0db40cf1";
                case IncludedShaderFile.ScreenUtils: return "c37ad380d74d9d24c8a999ec080e9167";
                case IncludedShaderFile.ColorGrading: return "70943f3e80f3d684c8bb849f9f026c2f";
                case IncludedShaderFile.SGSubTargetHelpers: return "72024cd79cc19a649a6b13a437b77eeb";
                case IncludedShaderFile.SurfaceDescriptionPre: return "6d676c29bfdd4d48bb6db8052581e15c";
                case IncludedShaderFile.SurfaceDescriptionPost: return "a0e9b46a9f8d47f5ae5d936bfe689f6f";
                case IncludedShaderFile.ShaderGraphUtils: return "0efd427a272f50f46869a2e176d66e6b";
            }
            Debug.LogError("Unknown ProPixelizer Shader include: " + shader);
            throw new UnknownShaderException();
        }

        public static string GetIncludePath(IncludedShaderFile shader)
        {
            var path = AssetDatabase.GUIDToAssetPath(GetGuid(shader));
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError(string.Format("Cannot find shader include file: {0}. This may indicate that the .meta files associated with the included shader files have changed.", shader));
                throw new CannotFindProPixelizerShaderIncludeException();
            }
            return path;
        }

        public class UnknownShaderException : Exception { }
        public class CannotFindProPixelizerShaderIncludeException : Exception { }
    }
}
#endif