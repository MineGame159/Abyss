using System.Numerics;
using Abyss.Core;
using Abyss.Gpu.Pipeline;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuCommandBuffer {
    public readonly GpuContext Ctx;

    public readonly CommandBuffer Handle;

    private GpuPipeline? boundPipeline;

    public GpuCommandBuffer(GpuContext ctx, CommandBuffer handle) {
        Ctx = ctx;
        Handle = handle;
    }

    public unsafe void Begin() {
        VkUtils.Wrap(Ctx.Vk.BeginCommandBuffer(Handle, new CommandBufferBeginInfo(
            flags: CommandBufferUsageFlags.OneTimeSubmitBit
        )), "Failed to begin a Command Buffer");
    }

    public void End() {
        VkUtils.Wrap(Ctx.Vk.EndCommandBuffer(Handle), "Failed to end a Command Buffer");
    }

    public void BindPipeline(GpuPipeline pipeline) {
        Ctx.Vk.CmdBindPipeline(Handle, pipeline.BindPoint, pipeline);
        boundPipeline = pipeline;
    }

    public unsafe void PushConstants<T>(in T data) where T : unmanaged {
        fixed (T* ptr = &data) {
            Ctx.Vk.CmdPushConstants(Handle, boundPipeline!.Layout, ShaderStageFlags.All, 0, (uint) Utils.SizeOf<T>(), ptr);
        }
    }

    public void BindDescriptorSet(uint index, DescriptorSet set, ReadOnlySpan<uint> offsets) {
        Ctx.Vk.CmdBindDescriptorSets(
            Handle,
            boundPipeline!.BindPoint,
            boundPipeline!.Layout,
            index,
            new ReadOnlySpan<DescriptorSet>(ref set),
            offsets
        );
    }

    public void BindDescriptorSet(uint index, params ReadOnlySpan<IDescriptor?> descriptors) {
        // Offsets

        var offsetCount = 0;

        foreach (var descriptor in descriptors)
            if (descriptor?.DescriptorInfo.Type == DescriptorType.UniformBuffer)
                offsetCount++;

        Span<uint> offsets = stackalloc uint[offsetCount];
        var i = 0;

        foreach (var descriptor in descriptors)
            switch (descriptor) {
                case GpuBuffer { DescriptorInfo.Type: DescriptorType.UniformBuffer }:
                    offsets[i++] = 0;
                    break;
                case GpuSubBuffer { DescriptorInfo.Type: DescriptorType.UniformBuffer } subBuffer:
                    offsets[i++] = (uint) subBuffer.Offset;
                    break;
            }

        // Bind

        var set = Ctx.Descriptors.GetSet(descriptors);
        BindDescriptorSet(index, set, offsets);
    }

    public void TraceRays(Sbt sbt, uint width, uint height, uint depth) {
        Ctx.RayTracingApi.CmdTraceRays(Handle, sbt.RayGen, sbt.Miss, sbt.Hit, sbt.Callable, width, height, depth);
    }

    public void CopyBuffer(GpuSubBuffer src, GpuSubBuffer dst) {
        if (src.Size != dst.Size)
            throw new Exception("CopyBuffer - buffers don't have matching size");

        Ctx.Vk.CmdCopyBuffer(Handle, src.Buffer, dst.Buffer, 1, new BufferCopy(
            src.Offset,
            dst.Offset,
            src.Size
        ));
    }

    public void CopyBuffer(GpuSubBuffer src, GpuImage dst) {
        Ctx.Vk.CmdCopyBufferToImage(Handle, src.Buffer, dst, ImageLayout.TransferDstOptimal, 1, new BufferImageCopy(
            src.Offset,
            0,
            0,
            new ImageSubresourceLayers(
                ImageAspectFlags.ColorBit,
                layerCount: 1
            ),
            new Offset3D(0, 0, 0),
            new Extent3D(dst.Size.X, dst.Size.Y, 1)
        ));
    }

    public void BlitImage(GpuImage src, GpuImage dst, Filter filter) {
        Ctx.Vk.CmdBlitImage(Handle, src, ImageLayout.General, dst, ImageLayout.General, 1,
            new ImageBlit {
                SrcSubresource = new ImageSubresourceLayers(
                    ImageAspectFlags.ColorBit,
                    layerCount: 1
                ),
                SrcOffsets = new ImageBlit.SrcOffsetsBuffer {
                    Element0 = new Offset3D(),
                    Element1 = new Offset3D((int) src.Size.X, (int) src.Size.Y, 1)
                },
                DstSubresource = new ImageSubresourceLayers(
                    ImageAspectFlags.ColorBit,
                    layerCount: 1
                ),
                DstOffsets = new ImageBlit.DstOffsetsBuffer {
                    Element0 = new Offset3D(),
                    Element1 = new Offset3D((int) dst.Size.X, (int) dst.Size.Y, 1)
                }
            }, filter);
    }

    public unsafe void TransitionImage(
        GpuImage image,
        ImageLayout layout,
        PipelineStageFlags srcStage, AccessFlags srcMask,
        PipelineStageFlags dstStage, AccessFlags dstMask
    ) {
        var aspectFlags = ImageAspectFlags.ColorBit;
        if (image.Usage.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit)) aspectFlags = ImageAspectFlags.DepthBit;

        Ctx.Vk.CmdPipelineBarrier(
            Handle,
            srcStage,
            dstStage,
            DependencyFlags.None,
            0, null,
            0, null,
            1, new ImageMemoryBarrier(
                srcAccessMask: srcMask,
                dstAccessMask: dstMask,
                oldLayout: image.Layout,
                newLayout: layout,
                image: image,
                subresourceRange: new ImageSubresourceRange(
                    aspectFlags,
                    levelCount: 1,
                    layerCount: 1
                )
            )
        );

        image.Layout = layout;
    }

    public unsafe void BufferBarrier(
        GpuSubBuffer buffer,
        PipelineStageFlags srcStage, AccessFlags srcMask,
        PipelineStageFlags dstStage, AccessFlags dstMask
    ) {
        Ctx.Vk.CmdPipelineBarrier(
            Handle,
            srcStage,
            dstStage,
            DependencyFlags.None,
            0, null,
            1, new BufferMemoryBarrier(
                srcAccessMask: srcMask,
                dstAccessMask: dstMask,
                buffer: buffer.Buffer,
                offset: buffer.Offset,
                size: buffer.Size
            ),
            0, null
        );
    }

    public unsafe void BeginRenderPass(params ReadOnlySpan<Attachment> attachments) {
        // Depth attachment

        var depthAttachmentRaw = default(RenderingAttachmentInfo);
        var hasDepthAttachment = false;

        foreach (var attachment in attachments) {
            if (!attachment.Image.Usage.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit))
                continue;

            if (hasDepthAttachment)
                throw new Exception("Multiple depth attachments");

            FillRawAttachment(attachment, out depthAttachmentRaw);
            hasDepthAttachment = true;
        }

        // Color attachments

        Span<RenderingAttachmentInfo> colorAttachmentsRaw =
            stackalloc RenderingAttachmentInfo[attachments.Length - (hasDepthAttachment ? 1 : 0)];

        var colorAttachmentI = 0;

        foreach (var attachment in attachments) {
            if (attachment.Image.Usage.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit))
                continue;

            FillRawAttachment(attachment, out colorAttachmentsRaw[colorAttachmentI++]);
        }

        // Command

        Ctx.Vk.CmdBeginRendering(Handle, new RenderingInfo(
            renderArea: new Rect2D(
                new Offset2D(0, 0),
                new Extent2D(attachments[0].Image.Size.X, attachments[0].Image.Size.Y)
            ),
            layerCount: 1,
            colorAttachmentCount: (uint) colorAttachmentsRaw.Length,
            pColorAttachments: Utils.AsPtr(colorAttachmentsRaw),
            pDepthAttachment: hasDepthAttachment ? &depthAttachmentRaw : null
        ));

        SetViewport(Vector2D<uint>.Zero, attachments[0].Image.Size);
        SetScissor(Vector2D<int>.Zero, attachments[0].Image.Size.As<int>());

        return;

        void FillRawAttachment(Attachment attachment, out RenderingAttachmentInfo raw) {
            raw = new RenderingAttachmentInfo(
                imageView: attachment.Image.View,
                imageLayout: attachment.Image.Usage.HasFlag(ImageUsageFlags.DepthStencilAttachmentBit)
                    ? ImageLayout.DepthAttachmentOptimal
                    : ImageLayout.ColorAttachmentOptimal,
                loadOp: attachment.LoadOp,
                storeOp: attachment.StoreOp
            );

            if (attachment.ClearValue != null)
                raw.ClearValue = new ClearValue(new ClearColorValue(
                    attachment.ClearValue.Value.X,
                    attachment.ClearValue.Value.Y,
                    attachment.ClearValue.Value.Z,
                    attachment.ClearValue.Value.W
                ));
        }
    }

    public void EndRenderPass() {
        Ctx.Vk.CmdEndRendering(Handle);
    }

    public void SetViewport(Vector2D<uint> pos, Vector2D<uint> size, bool flipY = false) {
        if (flipY)
            Ctx.Vk.CmdSetViewport(Handle, 0, 1, new Viewport(
                pos.X,
                size.Y,
                size.X,
                -size.Y,
                0,
                1
            ));
        else
            Ctx.Vk.CmdSetViewport(Handle, 0, 1, new Viewport(
                pos.X,
                pos.Y,
                size.X,
                size.Y,
                0,
                1
            ));
    }

    public void SetScissor(Vector2D<int> min, Vector2D<int> max) {
        Ctx.Vk.CmdSetScissor(Handle, 0, 1, new Rect2D(
            new Offset2D(min.X, min.Y),
            new Extent2D((uint) (max.X - min.X), (uint) (max.Y - min.Y))
        ));
    }

    public void BindVertexBuffer(GpuSubBuffer buffer) {
        Ctx.Vk.CmdBindVertexBuffers(Handle, 0, 1, buffer.Buffer, buffer.Offset);
    }

    public void BindIndexBuffer(GpuSubBuffer buffer, IndexType type) {
        Ctx.Vk.CmdBindIndexBuffer(Handle, buffer.Buffer, buffer.Offset, type);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
        Ctx.Vk.CmdDraw(Handle, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) {
        Ctx.Vk.CmdDrawIndexed(Handle, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
    }

    public unsafe void BeginGroup(string name) {
        var ptr = SilkMarshal.StringToPtr(name);

        Ctx.DebugUtilsApi.CmdBeginDebugUtilsLabel(Handle, new DebugUtilsLabelEXT(
            pLabelName: (byte*) ptr
        ));

        SilkMarshal.FreeString(ptr);
    }

    public void EndGroup() {
        Ctx.DebugUtilsApi.CmdEndDebugUtilsLabel(Handle);
    }

    public GpuQuery BeginQuery(PipelineStageFlags stage) {
        var query = Ctx.Queries.GetNext();
        query.Begin(this, stage);

        return query;
    }

    public void EndQuery(GpuQuery query, PipelineStageFlags stage) {
        query.End(this, stage);
    }

    public static implicit operator CommandBuffer(GpuCommandBuffer commandBuffer) {
        return commandBuffer.Handle;
    }
}

public readonly record struct Attachment(GpuImage Image, AttachmentLoadOp LoadOp, AttachmentStoreOp StoreOp, Vector4? ClearValue);