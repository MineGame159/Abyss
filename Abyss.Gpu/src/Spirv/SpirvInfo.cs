using Silk.NET.Vulkan;

namespace Abyss.Gpu.Spirv;

public class SpirvInfo {
    public readonly Binding[] Bindings;
    public readonly EntryPoint[] EntryPoints;

    public SpirvInfo(EntryPoint[] entryPoints, Binding[] bindings) {
        EntryPoints = entryPoints;
        Bindings = bindings;
    }

    public readonly record struct EntryPoint(ShaderStageFlags Stage, string Name);

    public readonly record struct Binding(uint Set, uint Index, DescriptorType Type);
}