using Silk.NET.Vulkan;

namespace Abyss.Gpu.Pipeline;

public abstract class GpuPipeline : IDisposable {
    public readonly GpuContext Ctx;
    public readonly Silk.NET.Vulkan.Pipeline Handle;

    public readonly PipelineLayout Layout;

    protected GpuPipeline(GpuContext ctx, PipelineLayout layout, Silk.NET.Vulkan.Pipeline handle) {
        Ctx = ctx;
        Layout = layout;
        Handle = handle;
    }

    public abstract PipelineBindPoint BindPoint { get; }

    public void Dispose() {
        unsafe {
            Ctx.Vk.DestroyPipeline(Ctx.Device, Handle, null);
        }

        GC.SuppressFinalize(this);
    }

    public static implicit operator Silk.NET.Vulkan.Pipeline(GpuPipeline pipeline) {
        return pipeline.Handle;
    }
}