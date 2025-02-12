using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuTextureArray : IDisposable {
    private readonly GpuContext ctx;

    private readonly DescriptorPool pool;
    public readonly DescriptorSet Set;

    private readonly GpuImageSampler?[] textures;
    
    public GpuTextureArray(GpuContext ctx, uint capacity) {
        this.ctx = ctx;

        unsafe {
            var size = new DescriptorPoolSize(
                Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
                capacity
            );
            
            VkUtils.Wrap(ctx.Vk.CreateDescriptorPool(ctx.Device, new DescriptorPoolCreateInfo(
                flags: DescriptorPoolCreateFlags.UpdateAfterBindBit,
                maxSets: 1,
                poolSizeCount: 1,
                pPoolSizes: &size
            ), null, out pool), "Failed to create a Descriptor Pool");
        }

        unsafe {
            var layout = ctx.Descriptors.GetLayout(new DescriptorInfo(DescriptorType.ImageSampler, (int) capacity));
            
            VkUtils.Wrap(ctx.Vk.AllocateDescriptorSets(ctx.Device, new DescriptorSetAllocateInfo(
                descriptorPool: pool,
                descriptorSetCount: 1,
                pSetLayouts: &layout
            ), out Set), "Failed to create a Descriptor Set");
        }

        textures = new GpuImageSampler?[capacity];
    }

    public uint Add(GpuImage image, Sampler sampler) {
        for (var i = 0; i < textures.Length; i++) {
            if (textures[i] != null)
                continue;

            textures[i] = new GpuImageSampler(image, sampler);

            unsafe {
                var write = new DescriptorImageInfo(
                    imageView: image.View,
                    imageLayout: ImageLayout.ShaderReadOnlyOptimal,
                    sampler: sampler
                );
                
                ctx.Vk.UpdateDescriptorSets(ctx.Device, 1, new WriteDescriptorSet(
                    dstSet: Set,
                    dstBinding: 0,
                    dstArrayElement: (uint) i,
                    descriptorCount: 1,
                    descriptorType: Silk.NET.Vulkan.DescriptorType.CombinedImageSampler,
                    pImageInfo: &write
                ), 0, null);
            }

            return (uint) i + 1;
        }

        throw new Exception("Texture array is full");
    }

    public unsafe void Dispose() {
        ctx.Vk.DestroyDescriptorPool(ctx.Device, pool, null);
        
        GC.SuppressFinalize(this);
    }
}