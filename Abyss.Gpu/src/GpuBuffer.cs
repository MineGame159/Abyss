using System.Diagnostics;
using Silk.NET.Vulkan;
using VMASharp;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Abyss.Gpu;

public class GpuBuffer : GpuResource, IDescriptor {
    public readonly Allocation Allocation;
    public readonly Buffer Handle;
    public readonly ulong Size;
    public readonly BufferUsageFlags Usage;

    public GpuBuffer(GpuContext ctx, Buffer handle, ulong size, BufferUsageFlags usage, Allocation allocation) : base(ctx) {
        Handle = handle;
        Size = size;
        Usage = usage;

        Allocation = allocation;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public unsafe ulong DeviceAddress => Ctx.Vk.GetBufferDeviceAddress(Ctx.Device, new BufferDeviceAddressInfo(buffer: Handle));

    // IDescriptor

    public DescriptorInfo DescriptorInfo {
        get {
            if (Usage.HasFlag(BufferUsageFlags.UniformBufferBit)) return DescriptorType.UniformBuffer;
            if (Usage.HasFlag(BufferUsageFlags.StorageBufferBit)) return DescriptorType.StorageBuffer;
    
            throw new Exception($"Buffer with {Usage} usage cannot be a descriptor");
        }
    }

    public bool DescriptorEquals(IDescriptor other) {
        return Equals(other);
    }

    public int DescriptorHashCode() {
        return GetHashCode();
    }

    public GpuSubBuffer Sub(ulong offset, ulong size) {
        return new GpuSubBuffer(this, offset, size);
    }

    public GpuSubBuffer Sub(ulong offset) {
        return new GpuSubBuffer(this, offset, Size - offset);
    }

    public unsafe Span<T> Map<T>() where T : unmanaged {
        return new Span<T>((void*) Allocation.Map(), (int) (Size / (ulong) sizeof(T)));
    }

    public void Unmap() {
        Allocation.Unmap();
    }

    public void Write<T>(ReadOnlySpan<T> data) where T : unmanaged {
        data.CopyTo(Map<T>());
        Unmap();
    }

    public void Write<T>(ref T data) where T : unmanaged {
        new ReadOnlySpan<T>(ref data).CopyTo(Map<T>());
        Unmap();
    }

    public override unsafe void Dispose() {
        Ctx.OnDestroyResource(this);

        Ctx.Vk.DestroyBuffer(Ctx.Device, Handle, null);
        Ctx.Allocator.FreeMemory(Allocation);

        GC.SuppressFinalize(this);
    }

    // Operators

    public static implicit operator Buffer(GpuBuffer buffer) {
        return buffer.Handle;
    }

    public static implicit operator GpuSubBuffer(GpuBuffer buffer) {
        return new GpuSubBuffer(buffer, 0, buffer.Size);
    }
}

public readonly record struct GpuSubBuffer(GpuBuffer Buffer, ulong Offset, ulong Size) : IDescriptor {
    // IDescriptor

    public DescriptorInfo DescriptorInfo => Buffer.DescriptorInfo;

    public bool DescriptorEquals(IDescriptor other) {
        if (other is GpuSubBuffer subBuffer) return Buffer == subBuffer.Buffer && Size == subBuffer.Size;

        return false;
    }

    public int DescriptorHashCode() {
        return HashCode.Combine(Buffer, Size);
    }

    public unsafe Span<T> Map<T>() where T : unmanaged {
        return new Span<T>((void*) ((ulong) Buffer.Allocation.Map() + Offset), (int) (Size / (ulong) sizeof(T)));
    }

    public void Unmap() {
        Buffer.Allocation.Unmap();
    }

    public void Write<T>(ReadOnlySpan<T> data) where T : unmanaged {
        data.CopyTo(Map<T>());
        Unmap();
    }

    public unsafe void Write<T>(in T data) where T : unmanaged {
        fixed (T* ptr = &data) {
            new ReadOnlySpan<T>(ptr, 1).CopyTo(Map<T>());
        }

        Unmap();
    }
}