// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProPixelizer
{
    /// <summary>
    /// Disables ProPixelizer shaders from being drawn.
    /// </summary>
    public struct SetProPixelizerPassVisibility : ProPixelizerCommandPassExecutor<bool>
    {
#if UNITY_2023_3_OR_NEWER
        public void ExecuteCommands(RasterCommandBuffer buffer, bool args)
#else
        public void ExecuteCommands(CommandBuffer buffer, bool args)
#endif
        {
            buffer.SetGlobalFloat(ProPixelizerFloats.PROPIXELIZER_PASS, args ? 1.0f : 0.0f);
        }
    }

    public class ProPixelizerSetPassVisibilityPass : ProPixelizerCommandBufferExecutorPass<SetProPixelizerPassVisibility, bool>
    {
        bool _Visibility;

        public ProPixelizerSetPassVisibilityPass(RenderPassEvent rpe, bool visibility) : base()
        {
            renderPassEvent = rpe;
            _Visibility = visibility;
        }

        protected override string ProfilingName => nameof(ProPixelizerSetPassVisibilityPass);
        public override bool GetArguments() => _Visibility;
    }
}
