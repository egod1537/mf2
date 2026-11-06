// Copyright Elliot Bentine, 2018-
using System;
using UnityEngine;

namespace ProPixelizer
{
    [Serializable]
    public sealed class ShaderResources
    {
        public Shader CopyDepth;
        public Shader PixelizationMap;
        public Shader ApplyPixelizationMap;
        public Shader FullScreenColorGrading;
        public Shader SceneRecomposition;
        public Shader OutlineDetection;

        public ShaderResources Load()
        {
            CopyDepth = Shader.Find(ProPixelizerShaderNames.COPY_DEPTH_SHADER);
            PixelizationMap = Shader.Find(ProPixelizerShaderNames.PIXELIZATION_MAP_SHADER);
            ApplyPixelizationMap = Shader.Find(ProPixelizerShaderNames.APPLY_PIXELIZATION_MAP_SHADER);
            FullScreenColorGrading = Shader.Find(ProPixelizerShaderNames.FULL_SCREEN_COLOR_GRADING_SHADER);
            SceneRecomposition = Shader.Find(ProPixelizerShaderNames.SCENE_RECOMPOSITION_SHADER);
            OutlineDetection = Shader.Find(ProPixelizerShaderNames.OUTLINE_DETECTION);
            return this;
        }
    }
}
