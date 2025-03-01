using Silk.NET.Vulkan;

namespace Abyss.Gpu.Pipeline;

public class GpuGraphicsPipeline : GpuPipeline {
    public readonly GpuGraphicsPipelineOptions Options;

    public GpuGraphicsPipeline(GpuContext ctx, PipelineLayout layout, Silk.NET.Vulkan.Pipeline handle, GpuGraphicsPipelineOptions options)
        : base(ctx, layout, handle) {
        Options = options;
    }

    public override PipelineBindPoint BindPoint => PipelineBindPoint.Graphics;
}

public readonly record struct GpuGraphicsPipelineOptions(
    PrimitiveTopology Topology,
    GpuShaderModule VertexShader,
    GpuShaderModule FragmentShader,
    VertexFormat Format,
    ColorAttachment[] ColorAttachments,
    DepthAttachment? DepthAttachment = null,
    PipelineLayout? Layout = null
);

public readonly record struct BlendMode(BlendFactor SrcColor, BlendFactor DstColor, BlendFactor SrcAlpha, BlendFactor DstAlpha) {
    public static readonly BlendMode Alpha = new(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendFactor.One, BlendFactor.Zero);
    public static readonly BlendMode Additive = new(BlendFactor.One, BlendFactor.One, BlendFactor.One, BlendFactor.Zero);
}

public readonly record struct ColorAttachment(Format Format, BlendMode? Blend);

public readonly record struct DepthAttachment(Format Format, CompareOp Compare, bool Write);