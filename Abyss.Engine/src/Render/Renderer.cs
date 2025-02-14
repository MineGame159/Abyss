using System.Diagnostics;
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
using Silk.NET.Maths;
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

    private readonly GrowableStorageBuffer<GpuMaterial> materials;
    private readonly Dictionary<Material, uint> materialIndices = [];

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
                new VertexAttribute(VertexAttributeType.Float, 2, false),
                new VertexAttribute(VertexAttributeType.Float, 3, false)
            ]),
            [
                new ColorAttachment(ctx.Swapchain.Images[0].Format, true)
            ],
            new DepthAttachment(Format.D32Sfloat, CompareOp.Less, true),
            ctx.Pipelines.GetLayout(
                (uint) Utils.SizeOf<DrawData>(),
                ctx.Descriptors.GetLayout(DescriptorType.UniformBuffer, DescriptorType.StorageBuffer),
                ctx.Descriptors.GetLayout(new DescriptorInfo(DescriptorType.ImageSampler, 128))
            )
        ));

        materials = new GrowableStorageBuffer<GpuMaterial>(ctx, 128);

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

        // Collect materials

        World.Query<Transform, MeshInstance>(renderEntityDesc, CollectEntityMaterial);
        materials.Upload(commandBuffer);

        // Render entities

        var frameUniforms = UploadFrameUniforms(out var clearColor);

        commandBuffer.BeginRenderPass(
            new Attachment(output, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(clearColor, 1)),
            new Attachment(depth, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(1))
        );

        commandBuffer.BindPipeline(pipeline);

        commandBuffer.BindDescriptorSet(0, frameUniforms, materials.Buffer);
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

    private GpuSubBuffer UploadFrameUniforms(out Vector3 clearColor) {
        var uniforms = new FrameUniforms();

        var entity = World.GetFirstEntity(cameraEntityDesc);

        if (entity != null) {
            ref var transform = ref entity.Value.Get<Transform>();
            ref var camera = ref entity.Value.Get<Camera>();

            uniforms.Projection = CreatePerspective(camera);
            uniforms.View = CreateLookAt(transform);
            uniforms.ProjectionView = uniforms.View * uniforms.Projection;

            uniforms.CameraPos = transform.Position;

            uniforms.Environment = new GpuWorldEnvironment {
                AmbientStrength = camera.Environment.AmbientStrength,
                SunColor = camera.Environment.SunColor,
                SunDirection = camera.Environment.SunDirection
            };

            clearColor = camera.Environment.ClearColor;
        }
        else {
            clearColor = Vector3.One;
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

    private void CollectEntityMaterial(ref Transform transform, ref MeshInstance instance) {
        GetMaterialIndex(instance.Material);
    }

    private void RenderEntity(ref Transform transform, ref MeshInstance instance) {
        var mesh = GetMesh(instance.Mesh);

        var positionMatrix = transform.Matrix;

        Matrix4x4.Invert(positionMatrix, out var invMatrix);
        var normalMatrix = Matrix4x4.Transpose(invMatrix);

        var data = new DrawData {
            PositionMatrix = positionMatrix,

            NormalMatrix = new Matrix3X4<float>(
                normalMatrix.M11, normalMatrix.M12, normalMatrix.M13, 0,
                normalMatrix.M21, normalMatrix.M22, normalMatrix.M23, 0,
                normalMatrix.M31, normalMatrix.M32, normalMatrix.M33, 0
            ),

            MaterialI = GetMaterialIndex(instance.Material)
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
            mesh = MeshBuilder.Create(ctx, asset);
            meshes[asset] = mesh;
        }

        return mesh;
    }

    private uint GetMaterialIndex(Material asset) {
        if (!materialIndices.TryGetValue(asset, out var index)) {
            var material = new GpuMaterial {
                Albedo = asset.Albedo,
                AlbedoTextureI = GetTextureIndex(asset.AlbedoMap)
            };

            index = materials.Add(material);
            materialIndices[asset] = index;
        }

        return index;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct FrameUniforms {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 ProjectionView;

        public AlignedVector3 CameraPos;

        public GpuWorldEnvironment Environment;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuWorldEnvironment {
        public AlignedVector3 SunColor;
        public Vector3 SunDirection;

        public float AmbientStrength;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuMaterial {
        public Vector4 Albedo;
        public uint AlbedoTextureI;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _0, _1, _2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawData {
        public Matrix4x4 PositionMatrix;
        public Matrix3X4<float> NormalMatrix;

        public uint MaterialI;
    }
}