using System.Numerics;
using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Engine.Assets;
using Abyss.Engine.Scene;
using Abyss.Gpu;
using Abyss.Gpu.Pipeline;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Silk.NET.Vulkan;
using VMASharp;
using DescriptorType = Abyss.Gpu.DescriptorType;

namespace Abyss.Engine.Render;

public class Renderer : BaseSystem<World, float> {
    private static readonly QueryDescription cameraEntityDesc = new QueryDescription().WithAll<Transform, Camera>();
    private static readonly QueryDescription renderEntityDesc = new QueryDescription().WithAll<Transform, MeshInstance>();

    private readonly GpuContext ctx;

    private readonly GpuGraphicsPipeline pipeline;
    private readonly Dictionary<IMesh, Mesh> meshes = [];
    private readonly GpuTextureArray textures;
    private readonly Dictionary<ITexture, uint> textureIndices = [];
    private readonly Sampler sampler;

    private GpuCommandBuffer commandBuffer = null!;
    private GpuImage output = null!;
    private GpuImage depth = null!;

    public Renderer(World world, GpuContext ctx) : base(world) {
        this.ctx = ctx;

        pipeline = ctx.Pipelines.Create(new GpuGraphicsPipelineOptions(
            PrimitiveTopology.TriangleList,
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.mesh.spv"),
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.mesh.spv"),
            new VertexFormat([
                new VertexAttribute(VertexAttributeType.Float, 3, false),
                new VertexAttribute(VertexAttributeType.Float, 2, false)
            ]),
            [
                new ColorAttachment(ctx.Swapchain.Images[0].Format, true)
            ],
            new DepthAttachment(Format.D32Sfloat, CompareOp.Less, true),
            ctx.Pipelines.GetLayout(
                (uint) Utils.SizeOf<DrawData>(),
                ctx.Descriptors.GetLayout(DescriptorType.UniformBuffer),
                ctx.Descriptors.GetLayout(new DescriptorInfo(DescriptorType.ImageSampler, 128))
            )
        ));

        textures = new GpuTextureArray(ctx, 128);

        sampler = ctx.CreateSampler(Filter.Linear, Filter.Linear, SamplerAddressMode.Repeat);
    }

    public void NewFrame(GpuCommandBuffer commandBuffer, GpuImage output) {
        this.commandBuffer = commandBuffer;
        this.output = output;

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (depth == null || depth.Size != output.Size) {
            depth?.Dispose();

            depth = ctx.CreateImage(
                output.Size,
                ImageUsageFlags.DepthStencilAttachmentBit,
                Format.D32Sfloat
            );
        }
    }

    public override void Update(in float t) {
        commandBuffer.TransitionImage(
            output,
            ImageLayout.PresentSrcKhr,
            PipelineStageFlags.BottomOfPipeBit, AccessFlags.None,
            PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit
        );

        commandBuffer.TransitionImage(
            depth,
            ImageLayout.DepthAttachmentOptimal,
            PipelineStageFlags.TopOfPipeBit, AccessFlags.None,
            PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit,
            AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit
        );

        // Render entities

        commandBuffer.BeginRenderPass(
            new Attachment(output, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(0.8f, 0.8f, 0.8f, 1)),
            new Attachment(depth, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(1))
        );

        commandBuffer.BindPipeline(pipeline);

        var frameUniforms = UploadFrameUniforms();

        commandBuffer.BindDescriptorSet(0, frameUniforms);
        commandBuffer.BindDescriptorSet(1, textures.Set, ReadOnlySpan<uint>.Empty);

        World.Query<Transform, MeshInstance>(renderEntityDesc, RenderEntity);

        commandBuffer.EndRenderPass();

        // Present

        commandBuffer.TransitionImage(
            output,
            ImageLayout.PresentSrcKhr,
            PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit,
            PipelineStageFlags.BottomOfPipeBit, AccessFlags.None
        );
    }

    private GpuSubBuffer UploadFrameUniforms() {
        var uniforms = new FrameUniforms();

        var entity = World.GetFirstEntity(cameraEntityDesc);

        if (entity != null) {
            ref var transform = ref entity.Value.Get<Transform>();
            ref var camera = ref entity.Value.Get<Camera>();

            uniforms.Projection = CreatePerspective(camera);
            uniforms.View = CreateLookAt(transform);
            uniforms.ProjectionView = uniforms.View * uniforms.Projection;
        }

        return ctx.FrameAllocator.Allocate(BufferUsageFlags.UniformBufferBit, uniforms);
    }

    private static Matrix4x4 CreateLookAt(in Transform transform) {
        var direction = Vector3.Transform(Vector3.UnitZ, transform.Rotation);
        return Matrix4x4.CreateLookTo(transform.Position, direction, Vector3.UnitY);
    }

    private Matrix4x4 CreatePerspective(in Camera camera) {
        var aspect = (float) output.Size.X / output.Size.Y;
        var tanHalfFovy = MathF.Tan(Utils.DegToRad(camera.Fov) / 2.0f);

        var m = new Matrix4x4 {
            [0, 0] = 1 / (aspect * tanHalfFovy),
            [1, 1] = -(1 / tanHalfFovy),
            [2, 2] = camera.Far / (camera.Near - camera.Far),
            [2, 3] = -1,
            [3, 2] = -(camera.Far * camera.Near) / (camera.Far - camera.Near)
        };

        return m;
    }

    private void RenderEntity(ref Transform transform, ref MeshInstance instance) {
        var mesh = GetMesh(instance.Mesh);

        var data = new DrawData {
            Transform = transform.Matrix,
            Albedo = instance.Material.Albedo,
            AlbedoTextureI = GetTextureIndex(instance.Material.AlbedoMap)
        };

        commandBuffer.PushConstants(data);
        commandBuffer.BindVertexBuffer(mesh.VertexBuffer);

        if (mesh.IndexBuffer != null) {
            commandBuffer.BindIndexBuffer(mesh.IndexBuffer, IndexType.Uint32);
            commandBuffer.DrawIndexed((uint) (mesh.IndexBuffer.Size / Utils.SizeOf<uint>()));
        }
        else {
            commandBuffer.Draw((uint) (mesh.VertexBuffer.Size / Utils.SizeOf<Vertex>()));
        }
    }

    private Mesh GetMesh(IMesh asset) {
        if (!meshes.TryGetValue(asset, out var mesh)) {
            mesh = new Mesh();

            if (asset.IndexCount != 0) {
                mesh.IndexBuffer = CreateBuffer<uint>(asset.IndexCount!.Value, asset.WriteIndices);
            }

            mesh.VertexBuffer = CreateBuffer<Vertex>(asset.VertexCount, asset.WriteVertices);

            meshes[asset] = mesh;
        }

        return mesh;
    }

    private uint GetTextureIndex(ITexture? asset) {
        if (asset == null)
            return 0;

        if (!textureIndices.TryGetValue(asset, out var index)) {
            var uploadBuffer = ctx.CreateBuffer(
                asset.Size.X * asset.Size.Y * Utils.SizeOf<Rgba>(),
                BufferUsageFlags.TransferSrcBit,
                MemoryUsage.CPU_Only
            );

            var data = uploadBuffer.Map<Rgba>();
            asset.Write(data);
            uploadBuffer.Unmap();

            var image = ctx.CreateImage(
                asset.Size,
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                Format.R8G8B8A8Unorm
            );

            ctx.Run(commandBuffer => {
                commandBuffer.TransitionImage(
                    image,
                    ImageLayout.TransferDstOptimal,
                    PipelineStageFlags.TopOfPipeBit, AccessFlags.None,
                    PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit
                );

                // ReSharper disable once AccessToDisposedClosure
                commandBuffer.CopyBuffer(uploadBuffer, image);

                commandBuffer.TransitionImage(
                    image,
                    ImageLayout.ShaderReadOnlyOptimal,
                    PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit,
                    PipelineStageFlags.FragmentShaderBit, AccessFlags.ShaderReadBit
                );
            });

            uploadBuffer.Dispose();

            index = textures.Add(image, sampler);
            textureIndices[asset] = index;
        }

        return index;
    }

    private GpuBuffer CreateBuffer<T>(uint count, Action<Span<T>> writer) where T : unmanaged {
        var usage = BufferUsageFlags.VertexBufferBit;
        if (typeof(T) == typeof(uint)) usage = BufferUsageFlags.IndexBufferBit;

        var uploadBuffer = ctx.CreateBuffer(
            count * Utils.SizeOf<T>(),
            BufferUsageFlags.TransferSrcBit,
            MemoryUsage.CPU_Only
        );

        var data = uploadBuffer.Map<T>();
        writer(data);
        uploadBuffer.Unmap();

        var buffer = ctx.CreateBuffer(
            uploadBuffer.Size,
            usage | BufferUsageFlags.TransferDstBit,
            MemoryUsage.GPU_Only
        );

        // ReSharper disable once AccessToDisposedClosure
        ctx.Run(commandBuffer => commandBuffer.CopyBuffer(uploadBuffer, buffer));

        uploadBuffer.Dispose();

        return buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameUniforms {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 ProjectionView;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawData {
        public Matrix4x4 Transform;
        public Vector4 Albedo;
        public uint AlbedoTextureI;
    }

    private class Mesh {
        public GpuBuffer? IndexBuffer;
        public GpuBuffer VertexBuffer = null!;
    }
}