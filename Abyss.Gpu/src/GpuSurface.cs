using Abyss.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Abyss.Gpu;

public class GpuSurface {
    public readonly KhrSurface Api;
    private readonly GpuContext ctx;
    public readonly SurfaceFormatKHR[] Formats;

    public readonly SurfaceKHR Handle;
    public readonly PresentModeKHR[] PresentModes;

    public GpuSurface(GpuContext ctx) {
        this.ctx = ctx;

        if (!ctx.Vk.TryGetInstanceExtension(ctx.Instance, out Api))
            throw new Exception("Failed to get Surface API");

        unsafe {
            Handle = ctx.VkSurface.Create<AllocationCallbacks>(new VkHandle(ctx.Instance.Handle), null).ToSurface();
        }

        unsafe {
            var count = 0u;
            Api.GetPhysicalDeviceSurfaceFormats(ctx.PhysicalDevice, Handle, ref count, null);

            Formats = new SurfaceFormatKHR[(int) count];
            Api.GetPhysicalDeviceSurfaceFormats(ctx.PhysicalDevice, Handle, ref count, Utils.AsPtr(Formats));
        }

        unsafe {
            var count = 0u;
            Api.GetPhysicalDeviceSurfacePresentModes(ctx.PhysicalDevice, Handle, ref count, null);

            PresentModes = new PresentModeKHR[(int) count];
            Api.GetPhysicalDeviceSurfacePresentModes(ctx.PhysicalDevice, Handle, ref count, Utils.AsPtr(PresentModes));
        }
    }

    public SurfaceCapabilitiesKHR Capabilities {
        get {
            VkUtils.Wrap(
                Api.GetPhysicalDeviceSurfaceCapabilities(ctx.PhysicalDevice, Handle, out var caps),
                "Failed to get surface capabilities"
            );

            return caps;
        }
    }

    public static implicit operator SurfaceKHR(GpuSurface surface) {
        return surface.Handle;
    }
}