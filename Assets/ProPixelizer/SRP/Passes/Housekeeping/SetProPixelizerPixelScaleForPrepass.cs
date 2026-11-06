// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// Disables ProPixelizer shaders from being drawn.
    /// </summary>
    public struct SetProPixelizerPixelScaleForPrepass : ProPixelizerCommandPassExecutor<ProPixelizerSettings>
    {
#if UNITY_2023_3_OR_NEWER
        public void ExecuteCommands(RasterCommandBuffer buffer, ProPixelizerSettings args)
#else
        public void ExecuteCommands(CommandBuffer buffer, ProPixelizerSettings args)
#endif
        {
            if (args.UsePixelExpansion)
                ProPixelizerUtils.SetPixelGlobalScale(buffer, 1f);
            else
                ProPixelizerUtils.SetPixelGlobalScale(buffer, 0.01f);
        }
    }

    public class ProPixelizerSetPixelScaleForPrepassPass : ProPixelizerCommandBufferExecutorPass<SetProPixelizerPixelScaleForPrepass, ProPixelizerSettings>
    {
        public ProPixelizerSetPixelScaleForPrepassPass() : base()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        }

        protected override string ProfilingName => nameof(ProPixelizerSetPixelScaleForPrepassPass);
        public override ProPixelizerSettings GetArguments()
        {
            return Settings;
        }
    }
}
