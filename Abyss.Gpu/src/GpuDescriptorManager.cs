using System.Diagnostics.CodeAnalysis;
using Abyss.Core;
using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuDescriptorManager {
    private readonly GpuContext ctx;

    private readonly MultiKeyDictionary<DescriptorType?, DescriptorSetLayout> layouts = new();

    private readonly DescriptorPool pool;
    private readonly MultiKeyDictionary<IDescriptor?, DescriptorSet> sets = new(DescriptorEqualityComparer.Instance);

    public GpuDescriptorManager(GpuContext ctx) {
        this.ctx = ctx;

        List<DescriptorPoolSize> poolSizes = [
            new(DescriptorType.UniformBufferDynamic, 100),
            new(DescriptorType.StorageBuffer, 100),
            new(DescriptorType.StorageImage, 100),
            new(DescriptorType.CombinedImageSampler, 100)
        ];

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ctx.AccelStructApi != null) {
            poolSizes.Add(new DescriptorPoolSize(DescriptorType.AccelerationStructureKhr, 100));
        }

        unsafe {
            VkUtils.Wrap(ctx.Vk.CreateDescriptorPool(this.ctx.Device, new DescriptorPoolCreateInfo(
                flags: DescriptorPoolCreateFlags.FreeDescriptorSetBit,
                maxSets: 100,
                poolSizeCount: (uint) poolSizes.Count,
                pPoolSizes: Utils.AsPtr(poolSizes)
            ), null, out pool), "Failed to create a Descriptor Pool");
        }
    }

    internal void OnDestroyResource(GpuResource resource) {
        if (resource is IDescriptor descriptor) {
            foreach (var pair in sets)
                if (pair.Keys.Contains(descriptor))
                    VkUtils.Wrap(
                        ctx.Vk.FreeDescriptorSets(ctx.Device, pool, 1, pair.Value),
                        "Failed to free a Descriptor Set"
                    );

            sets.Remove(descriptors => descriptors.Contains(descriptor, DescriptorEqualityComparer.Instance));
        }
    }

    public unsafe DescriptorSetLayout GetLayout(ReadOnlySpan<DescriptorType?> types) {
        if (!layouts.TryGetValue(types, out var layout)) {
            Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[types.Length];

            for (var i = 0; i < types.Length; i++) {
                var type = types[i];

                bindings[i] = new DescriptorSetLayoutBinding(
                    (uint) i,
                    type ?? 0,
                    type == null ? 0u : 1u,
                    ShaderStageFlags.All
                );
            }

            VkUtils.Wrap(ctx.Vk.CreateDescriptorSetLayout(ctx.Device, new DescriptorSetLayoutCreateInfo(
                bindingCount: (uint) bindings.Length,
                pBindings: Utils.AsPtr(bindings)
            ), null, out layout), "Failed to create a Descriptor Set Layout");

            layouts[types] = layout;
        }

        return layout;
    }

    public DescriptorSetLayout GetLayout(ReadOnlySpan<IDescriptor?> descriptors) {
        Span<DescriptorType?> types = stackalloc DescriptorType?[descriptors.Length];

        for (var i = 0; i < types.Length; i++) types[i] = descriptors[i]?.DescriptorType;

        return GetLayout(types);
    }

    [SuppressMessage("ReSharper", "StackAllocInsideLoop")]
    public unsafe DescriptorSet GetSet(ReadOnlySpan<IDescriptor?> descriptors) {
        if (!sets.TryGetValue(descriptors, out var set)) {
            var layout = GetLayout(descriptors);

            VkUtils.Wrap(ctx.Vk.AllocateDescriptorSets(ctx.Device, new DescriptorSetAllocateInfo(
                descriptorPool: pool,
                descriptorSetCount: 1,
                pSetLayouts: &layout
            ), out set), "Failed to allocate a Descriptor Set");

            Span<WriteDescriptorSet> writes = stackalloc WriteDescriptorSet[descriptors.NonNullCount()];
            var i = 0;

            foreach (var descriptor in descriptors) {
                if (descriptor == null)
                    continue;

                ref var write = ref writes[i];

                write = new WriteDescriptorSet(
                    dstSet: set,
                    dstBinding: (uint) i,
                    descriptorCount: 1,
                    descriptorType: descriptor.DescriptorType
                );

                switch (descriptors[i++]) {
                    case GpuBuffer buffer: {
                        var info = stackalloc DescriptorBufferInfo[1];
                        *info = new DescriptorBufferInfo(
                            buffer,
                            range: Vk.WholeSize
                        );

                        write.PBufferInfo = info;
                        break;
                    }
                    case GpuSubBuffer subBuffer: {
                        var info = stackalloc DescriptorBufferInfo[1];
                        *info = new DescriptorBufferInfo(
                            subBuffer.Buffer,
                            range: subBuffer.Size
                        );

                        write.PBufferInfo = info;
                        break;
                    }
                    case GpuImage image: {
                        var info = stackalloc DescriptorImageInfo[1];
                        *info = new DescriptorImageInfo(
                            imageView: image,
                            imageLayout: ImageLayout.General
                        );

                        write.PImageInfo = info;
                        break;
                    }
                    case GpuSamplerImage samplerImage: {
                        var info = stackalloc DescriptorImageInfo[1];
                        *info = new DescriptorImageInfo(
                            imageView: samplerImage.Image,
                            imageLayout: ImageLayout.ShaderReadOnlyOptimal,
                            sampler: samplerImage.Sampler
                        );

                        write.PImageInfo = info;
                        break;
                    }
                    case GpuAccelStruct accelStruct: {
                        var handle = stackalloc AccelerationStructureKHR[1];
                        *handle = accelStruct.Handle;

                        var info = stackalloc WriteDescriptorSetAccelerationStructureKHR[1];
                        *info = new WriteDescriptorSetAccelerationStructureKHR(
                            accelerationStructureCount: 1,
                            pAccelerationStructures: handle
                        );

                        write.PNext = info;
                        break;
                    }
                }
            }

            ctx.Vk.UpdateDescriptorSets(ctx.Device, (uint) writes.Length, Utils.AsPtr(writes), 0, null);

            sets[descriptors] = set;
        }

        return set;
    }
}