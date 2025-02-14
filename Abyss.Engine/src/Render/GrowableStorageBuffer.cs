using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Gpu;
using Silk.NET.Vulkan;
using VMASharp;

namespace Abyss.Engine.Render;

public class GrowableStorageBuffer<T> : IDisposable where T : unmanaged {
    private readonly GpuContext ctx;

    public GpuBuffer Buffer { get; private set; }

    private readonly List<T> items = [];

    public GrowableStorageBuffer(GpuContext ctx, uint initialCapacity) {
        this.ctx = ctx;

        Buffer = ctx.CreateBuffer(
            initialCapacity * Utils.SizeOf<T>(),
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.GPU_Only
        );
    }

    public uint Add(T item) {
        items.Add(item);
        return (uint) items.Count - 1;
    }

    public void Upload(GpuCommandBuffer commandBuffer) {
        var uploadBuffer = ctx.FrameAllocator.Allocate<T>(BufferUsageFlags.TransferSrcBit, CollectionsMarshal.AsSpan(items));

        if (uploadBuffer.Size > Buffer.Size) {
            Buffer.Dispose();

            Buffer = ctx.CreateBuffer(
                uploadBuffer.Size,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.GPU_Only
            );
        }

        commandBuffer.CopyBuffer(uploadBuffer, Buffer.Sub(0, uploadBuffer.Size));

        commandBuffer.BufferBarrier(
            Buffer,
            PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit,
            PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit
        );
    }

    public void Dispose() {
        Buffer.Dispose();

        GC.SuppressFinalize(this);
    }
}