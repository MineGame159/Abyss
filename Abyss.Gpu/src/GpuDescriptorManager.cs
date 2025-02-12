using System.Diagnostics.CodeAnalysis;
using Abyss.Core;
using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuDescriptorManager {
    private readonly GpuContext ctx;

    private readonly MultiKeyDictionary<DescriptorInfo?, DescriptorSetLayout> layouts = new();

    private readonly DescriptorPool pool;
    private readonly MultiKeyDictionary<IDescriptor?, DescriptorSet> sets = new(DescriptorEqualityComparer.Instance);

    public GpuDescriptorManager(GpuContext ctx) {
        this.ctx = ctx;

        var poolSizes = new List<DescriptorPoolSize>();

        foreach (var type in Enum.GetValues<DescriptorType>()) {
            if (type != DescriptorType.AccelStruct || ctx.AccelStructApi != null) {
                poolSizes.Add(new DescriptorPoolSize(type.Vk(), 100));
            }
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

    public unsafe DescriptorSetLayout GetLayout(params ReadOnlySpan<DescriptorInfo?> infos) {
        if (!layouts.TryGetValue(infos, out var layout)) {
            Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[infos.Length];
            Span<DescriptorBindingFlags> flags = stackalloc DescriptorBindingFlags[infos.Length];
            var hasArray = false;

            for (var i = 0; i < infos.Length; i++) {
                var info = infos[i];

                if (info is { Count: < 1 })
                    throw new Exception("Invalid descriptor count");

                if (info is { Count: > 1 }) {
                    flags[i] = DescriptorBindingFlags.UpdateAfterBindBit | DescriptorBindingFlags.PartiallyBoundBit;
                    hasArray = true;
                }

                bindings[i] = new DescriptorSetLayoutBinding(
                    (uint) i,
                    info?.Type.Vk() ?? 0,
                    (uint) (info?.Count ?? 1),
                    ShaderStageFlags.All
                );
            }

            var flagInfo = new DescriptorSetLayoutBindingFlagsCreateInfo(
                bindingCount: (uint) flags.Length,
                pBindingFlags: Utils.AsPtr(flags)
            );

            VkUtils.Wrap(ctx.Vk.CreateDescriptorSetLayout(ctx.Device, new DescriptorSetLayoutCreateInfo(
                pNext: &flagInfo,
                flags: hasArray ? DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit : DescriptorSetLayoutCreateFlags.None,
                bindingCount: (uint) bindings.Length,
                pBindings: Utils.AsPtr(bindings)
            ), null, out layout), "Failed to create a Descriptor Set Layout");

            layouts[infos] = layout;
        }

        return layout;
    }

    public DescriptorSetLayout GetLayout(params ReadOnlySpan<IDescriptor?> descriptors) {
        Span<DescriptorInfo?> infos = stackalloc DescriptorInfo?[descriptors.Length];

        for (var i = 0; i < infos.Length; i++)
            infos[i] = descriptors[i]?.DescriptorInfo;

        return GetLayout(infos);
    }

    [SuppressMessage("ReSharper", "StackAllocInsideLoop")]
    public unsafe DescriptorSet GetSet(params ReadOnlySpan<IDescriptor?> descriptors) {
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
                    descriptorType: descriptor.DescriptorInfo.Type.Vk()
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
                    case GpuImageSampler imageSampler: {
                        var info = stackalloc DescriptorImageInfo[1];
                        *info = new DescriptorImageInfo(
                            imageView: imageSampler.Image,
                            imageLayout: ImageLayout.ShaderReadOnlyOptimal,
                            sampler: imageSampler.Sampler
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
                    default: {
                        throw new Exception("Invalid descriptor");
                    }
                }
            }

            ctx.Vk.UpdateDescriptorSets(ctx.Device, (uint) i, Utils.AsPtr(writes), 0, null);

            sets[descriptors] = set;
        }

        return set;
    }
}