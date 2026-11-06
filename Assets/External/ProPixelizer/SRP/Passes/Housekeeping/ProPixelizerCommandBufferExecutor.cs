// Copyright Elliot Bentine, 2018-
using UnityEngine.Rendering;
#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif
using UnityEngine.Rendering.Universal;

#pragma warning disable 0672

namespace ProPixelizer
{
    /// <summary>
    /// Provides a way to abstract out boilerplate code for executing command buffers across 2022.3 and Unity 6, 
    /// in a manner that will not allocate. <summary>
    /// </summary>
    public interface ProPixelizerCommandPassExecutor<Args> where Args : struct
    {
#if UNITY_2023_3_OR_NEWER
        void ExecuteCommands(RasterCommandBuffer buffer, Args args);
#else
        void ExecuteCommands(CommandBuffer buffer, Args args);
#endif
    }

    /// <summary>
    /// Provides a wrapper for setting parameters in a specific render pass event.
    /// </summary>
    public abstract class ProPixelizerCommandBufferExecutorPass<E, P> : ProPixelizerPass
        where E : struct, ProPixelizerCommandPassExecutor<P>
        where P : struct
    {
        /// <summary>
        /// Returns a set of arguments for the executor.
        /// </summary>
        /// <returns></returns>
        public abstract P GetArguments();
        protected abstract string ProfilingName { get; }

        #region non-RG
        #if !PRERG_API_REMOVED
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer buffer = CommandBufferPool.Get(ProfilingName);
            var executor = new E();
            var args = GetArguments();
#if UNITY_2023_3_OR_NEWER
            executor.ExecuteCommands(CommandBufferHelpers.GetRasterCommandBuffer(buffer), args);
#else
            executor.ExecuteCommands(buffer, args);
#endif
            context.ExecuteCommandBuffer(buffer);
            CommandBufferPool.Release(buffer);
        }
        #endif
        #endregion

        #region RG
#if UNITY_2023_3_OR_NEWER
        protected class PassData
        {
            public P Args;

            public PassData()
            {
            }
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(ProfilingName, out var passData))
            {
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                passData.Args = GetArguments();

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var executor = new E();
                    executor.ExecuteCommands(context.cmd, data.Args);
                });
            }
        }
#endif
#endregion
    }
}
