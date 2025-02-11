using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuCommandPool {
    private readonly GpuContext ctx;
    private readonly CommandPool pool;

    public unsafe GpuCommandPool(GpuContext ctx) {
        this.ctx = ctx;

        VkUtils.Wrap(
            ctx.Vk.CreateCommandPool(ctx.Device, new CommandPoolCreateInfo(
                queueFamilyIndex: VkUtils.GetQueueIndices(ctx.Vk, ctx.PhysicalDevice).Graphics!.Value
            ), null, out pool),
            "Failed to create Command Pool"
        );
    }

    public void Reset() {
        VkUtils.Wrap(
            ctx.Vk.ResetCommandPool(ctx.Device, pool, CommandPoolResetFlags.None),
            "Failed to reset Command Pool"
        );
    }

    public unsafe GpuCommandBuffer Get() {
        VkUtils.Wrap(
            ctx.Vk.AllocateCommandBuffers(ctx.Device, new CommandBufferAllocateInfo(
                commandPool: pool,
                level: CommandBufferLevel.Primary,
                commandBufferCount: 1
            ), out var handle),
            "Failed to allocate Command Buffer"
        );

        return new GpuCommandBuffer(ctx, handle);
    }
}