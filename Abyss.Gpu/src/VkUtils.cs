using Abyss.Core;
using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public static class VkUtils {
    public static void Wrap(Result result, string message) {
        if (result != Result.Success)
            throw new Exception(message + ": " + result);
    }

    public static unsafe QueueIndices GetQueueIndices(Vk vk, PhysicalDevice physicalDevice) {
        var count = 0u;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, null);

        Span<QueueFamilyProperties> families = stackalloc QueueFamilyProperties[(int) count];
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, ref count, Utils.AsPtr(families));

        uint? graphics = null;

        for (var i = 0; i < count; i++) {
            var props = families[i];

            if (props.QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                graphics = (uint) i;
        }

        return new QueueIndices(graphics);
    }

    public readonly record struct QueueIndices(uint? Graphics) {
        public bool Valid => Graphics.HasValue;
    }
}