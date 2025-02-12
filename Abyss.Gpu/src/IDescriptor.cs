using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;

namespace Abyss.Gpu;

public enum DescriptorType {
    UniformBuffer,
    StorageBuffer,
    ImageSampler,
    StorageImage,
    AccelStruct
}

public readonly record struct DescriptorInfo(DescriptorType Type, int Count) {
    public static implicit operator DescriptorInfo(DescriptorType type) => new(type, 1);
}

public interface IDescriptor {
    public DescriptorInfo DescriptorInfo { get; }

    public bool DescriptorEquals(IDescriptor other);

    public int DescriptorHashCode();
}

public class DescriptorEqualityComparer : EqualityComparer<IDescriptor?> {
    public static readonly DescriptorEqualityComparer Instance = new();

    private DescriptorEqualityComparer() { }

    public override bool Equals(IDescriptor? x, IDescriptor? y) {
        if (x == null && y == null)
            return true;

        if (x == null || y == null)
            return false;

        return x.DescriptorEquals(y);
    }

    public override int GetHashCode(IDescriptor obj) {
        return obj.DescriptorHashCode();
    }
}

public static class DescriptorTypeExt {
    public static VkDescriptorType Vk(this DescriptorType type) {
        return type switch {
            DescriptorType.UniformBuffer => VkDescriptorType.UniformBufferDynamic,
            DescriptorType.StorageBuffer => VkDescriptorType.StorageBuffer,
            DescriptorType.ImageSampler => VkDescriptorType.CombinedImageSampler,
            DescriptorType.StorageImage => VkDescriptorType.StorageImage,
            DescriptorType.AccelStruct => VkDescriptorType.AccelerationStructureKhr,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
}