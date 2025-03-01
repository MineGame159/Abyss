using Abyss.Engine.Assets;
using Abyss.Gpu;
using Abyss.Gpu.Pipeline;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace Abyss.Engine.Render;

internal class Bloom {
    private readonly GpuContext ctx;

    private readonly GpuGraphicsPipeline downsamplePipeline;
    private readonly GpuGraphicsPipeline upsamplePipeline;

    private readonly Sampler sampler;

    private Vector2D<uint> imageSize;
    private readonly BloomImage[] images;

    public Bloom(GpuContext ctx) {
        this.ctx = ctx;

        downsamplePipeline = ctx.Pipelines.Create(new GpuGraphicsPipelineOptions(
            PrimitiveTopology.TriangleList,
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.fullscreen.spv"),
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.bloom_downsample.spv"),
            new VertexFormat([]),
            [
                new ColorAttachment(Format.R16G16B16A16Sfloat, null)
            ]
        ));

        upsamplePipeline = ctx.Pipelines.Create(new GpuGraphicsPipelineOptions(
            PrimitiveTopology.TriangleList,
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.fullscreen.spv"),
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.bloom_upsample.spv"),
            new VertexFormat([]),
            [
                new ColorAttachment(Format.R16G16B16A16Sfloat, BlendMode.Additive)
            ]
        ));

        sampler = ctx.CreateSampler(Filter.Linear, Filter.Linear, SamplerAddressMode.ClampToEdge);

        imageSize = Vector2D<uint>.Zero;
        images = new BloomImage[7];
    }

    public void Render(GpuCommandBuffer commandBuffer, GpuImage color, WorldEnvironment env) {
        if (imageSize != color.Size)
            RecreateImages(color.Size);

        images[0] = new BloomImage(color, PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit);

        for (var i = 1; i < images.Length; i++) {
            images[i].Stage = PipelineStageFlags.TopOfPipeBit;
            images[i].Access = AccessFlags.None;
        }

        commandBuffer.BeginGroup("Bloom");

        Downsample(commandBuffer, env.BloomThreshold);
        Upsample(commandBuffer);

        commandBuffer.EndGroup();
    }

    private void Downsample(GpuCommandBuffer commandBuffer, float threshold) {
        commandBuffer.BindPipeline(downsamplePipeline);
        commandBuffer.PushConstants(threshold);

        for (var i = 0; i < images.Length - 1; i++) {
            ref var src = ref images[i];
            ref var dst = ref images[i + 1];

            src.Transition(
                commandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit
            );

            dst.Transition(
                commandBuffer,
                ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit
            );

            commandBuffer.BeginRenderPass(new Attachment(dst, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, null));

            commandBuffer.BindDescriptorSet(0, new GpuImageSampler(src, sampler));
            commandBuffer.Draw(3);

            commandBuffer.EndRenderPass();

            if (i == 0) {
                commandBuffer.PushConstants(-1f);
            }
        }
    }

    private void Upsample(GpuCommandBuffer commandBuffer) {
        commandBuffer.BindPipeline(upsamplePipeline);
        commandBuffer.PushConstants(0.005f);

        for (var i = images.Length - 1; i > 0; i--) {
            ref var src = ref images[i];
            ref var dst = ref images[i - 1];

            src.Transition(
                commandBuffer,
                ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit
            );

            dst.Transition(
                commandBuffer,
                ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit
            );

            commandBuffer.BeginRenderPass(new Attachment(dst, AttachmentLoadOp.Load, AttachmentStoreOp.Store, null));

            commandBuffer.BindDescriptorSet(0, new GpuImageSampler(src, sampler));
            commandBuffer.Draw(3);

            commandBuffer.EndRenderPass();
        }
    }

    private void RecreateImages(Vector2D<uint> size) {
        imageSize = size;

        for (var i = 1; i < images.Length; i++) {
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            images[i].Image?.Dispose();
        }

        for (var i = 1; i < images.Length; i++) {
            size /= 2;

            images[i] = new BloomImage(ctx.CreateImage(
                size,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                Format.R16G16B16A16Sfloat
            ), PipelineStageFlags.None, AccessFlags.None);
        }
    }

    private record struct BloomImage(GpuImage Image, PipelineStageFlags Stage, AccessFlags Access) {
        public void Transition(GpuCommandBuffer commandBuffer, ImageLayout layout, PipelineStageFlags stage, AccessFlags access) {
            commandBuffer.TransitionImage(
                Image,
                layout,
                Stage, Access,
                stage, access
            );

            Stage = stage;
            Access = access;
        }

        public static implicit operator GpuImage(BloomImage image) => image.Image;
    }
}