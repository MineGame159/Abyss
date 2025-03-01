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
    private static readonly QueryDescription cameraEntityDesc = new QueryDescription()
        .WithAll<Transform, Camera>();

    private static readonly QueryDescription renderEntityDesc = new QueryDescription()
        .WithAll<Transform, MeshInstance>();

    private static readonly QueryDescription lightEntityDesc = new QueryDescription()
        .WithAll<Transform>()
        .WithAny<PointLight, DirectionalLight>();

    public readonly GpuContext Ctx;

    private readonly GpuGraphicsPipeline pipeline;

    private readonly Dictionary<IMesh, Mesh> meshes = [];

    private readonly GrowableStorageBuffer<GpuLight> lights;

    private readonly GrowableStorageBuffer<GpuMaterial> materials;
    private readonly Dictionary<Material, uint> materialIndices = [];

    private readonly List<Entity> opaqueEntities = [];
    private readonly List<Entity> translucentEntities = [];

    private readonly GpuTextureArray textures;
    private readonly Dictionary<ITexture, uint> textureIndices = [];

    private readonly Sampler sampler;

    private GpuCommandBuffer commandBuffer = null!;
    private GpuImage output = null!;
    private GpuImage depth = null!;

    public Matrix4x4 ProjectionMatrix { get; private set; }
    public Matrix4x4 ViewMatrix { get; private set; }

    public Renderer(World world, GpuContext ctx) : base(world) {
        this.Ctx = ctx;

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
            new DepthAttachment(Format.D32Sfloat, CompareOp.LessOrEqual, true),
            ctx.Pipelines.GetLayout(
                (uint) Utils.SizeOf<DrawData>(),
                ctx.Descriptors.GetLayout(DescriptorType.UniformBuffer, DescriptorType.StorageBuffer, DescriptorType.StorageBuffer),
                ctx.Descriptors.GetLayout(new DescriptorInfo(DescriptorType.ImageSampler, 128))
            )
        ));

        lights = new GrowableStorageBuffer<GpuLight>(ctx, 64);

        materials = new GrowableStorageBuffer<GpuMaterial>(ctx, 256);

        textures = new GpuTextureArray(ctx, 128);

        sampler = ctx.CreateSampler(Filter.Linear, Filter.Linear, SamplerAddressMode.Repeat);
    }

    public void NewFrame(GpuCommandBuffer commandBuffer, GpuImage output) {
        this.commandBuffer = commandBuffer;
        this.output = output;

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (depth == null || depth.Size != output.Size) {
            depth?.Dispose();

            depth = Ctx.CreateImage(
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

        // Uniforms

        var frameUniforms = GetFrameUniforms(out var clearColor);
        var frameUniformsBuffer = Ctx.FrameAllocator.Allocate(BufferUsageFlags.UniformBufferBit, frameUniforms);

        // Collect lights

        lights.Clear();

        World.Query(lightEntityDesc, CollectEntityLight);

        lights.Upload(commandBuffer);

        // Collect render entities

        materials.Clear();
        materialIndices.Clear();

        opaqueEntities.Clear();
        translucentEntities.Clear();

        World.Query<Transform, MeshInstance>(renderEntityDesc, CollectRenderEntity);

        opaqueEntities.Sort(CloserEntityComparison);
        translucentEntities.Sort(FurtherEntityComparison);

        materials.Upload(commandBuffer);

        // Render entities

        commandBuffer.BeginRenderPass(
            new Attachment(output, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(clearColor, 1)),
            new Attachment(depth, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, new Vector4(1))
        );

        commandBuffer.BindPipeline(pipeline);

        commandBuffer.BindDescriptorSet(0, frameUniformsBuffer, lights.Buffer, materials.Buffer);
        commandBuffer.BindDescriptorSet(1, textures.Set, ReadOnlySpan<uint>.Empty);

        commandBuffer.BeginGroup("Opaque");

        foreach (var entity in opaqueEntities) {
            RenderEntity(entity);
        }

        commandBuffer.EndGroup();
        commandBuffer.BeginGroup("Translucent");

        foreach (var entity in translucentEntities) {
            RenderEntity(entity);
        }

        commandBuffer.EndGroup();

        commandBuffer.EndRenderPass();

        return;

        int CloserEntityComparison(Entity e1, Entity e2) {
            ref var t1 = ref e1.Get<Transform>();
            ref var t2 = ref e2.Get<Transform>();

            var d1 = Vector3.DistanceSquared(frameUniforms.CameraPos, t1.Position);
            var d2 = Vector3.DistanceSquared(frameUniforms.CameraPos, t2.Position);

            return d1.CompareTo(d2);
        }

        int FurtherEntityComparison(Entity e1, Entity e2) {
            ref var t1 = ref e1.Get<Transform>();
            ref var t2 = ref e2.Get<Transform>();

            var d1 = Vector3.DistanceSquared(frameUniforms.CameraPos, t1.Position);
            var d2 = Vector3.DistanceSquared(frameUniforms.CameraPos, t2.Position);

            return d2.CompareTo(d1);
        }
    }

    private FrameUniforms GetFrameUniforms(out Vector3 clearColor) {
        var uniforms = new FrameUniforms {
            LightCount = lights.Count
        };

        var entity = World.GetFirstEntity(cameraEntityDesc);

        if (entity != null) {
            ref var transform = ref entity.Value.Get<Transform>();
            ref var camera = ref entity.Value.Get<Camera>();

            uniforms.Projection = CreatePerspective(camera);
            uniforms.View = CreateLookAt(transform);
            uniforms.ProjectionView = uniforms.View * uniforms.Projection;

            uniforms.CameraPos = transform.Position;

            clearColor = camera.Environment.ClearColor;

            var aspect = (float) output.Size.X / output.Size.Y;
            ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(Utils.DegToRad(camera.Fov), aspect, camera.Near, camera.Far);
            ViewMatrix = uniforms.View;
        }
        else {
            clearColor = Vector3.One;
        }

        return uniforms;
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

    private void CollectEntityLight(Entity entity) {
        if (entity.TryGet(out Info info) && !info.Visible)
            return;

        if (entity.TryGet(out PointLight point)) {
            lights.Add(new GpuLight {
                Type = GpuLightType.Point,
                Color = point.Color * point.Intensity,
                Data = WorldTransforms.Get(entity)!.Value.Position
            });
        }
        else {
            ref var directional = ref entity.Get<DirectionalLight>();

            lights.Add(new GpuLight {
                Type = GpuLightType.Directional,
                Color = directional.Color * directional.Intensity,
                Data = -Vector3.Normalize(directional.Direction)
            });
        }
    }

    private void CollectRenderEntity(Entity entity, ref Transform transform, ref MeshInstance instance) {
        if (entity.TryGet(out Info info) && info.Visible) {
            GetMaterialIndex(instance.Material);

            if (instance.Material.Opaque) opaqueEntities.Add(entity);
            else translucentEntities.Add(entity);
        }
    }

    private void RenderEntity(Entity entity) {
        var transform = WorldTransforms.Get(entity)!.Value;
        ref var instance = ref entity.Get<MeshInstance>();

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
            mesh = MeshBuilder.Create(Ctx, asset);
            meshes[asset] = mesh;
        }

        return mesh;
    }

    private uint GetMaterialIndex(Material asset) {
        if (!materialIndices.TryGetValue(asset, out var index)) {
            var material = new GpuMaterial {
                Albedo = new Vector3(asset.Albedo.X, asset.Albedo.Y, asset.Albedo.Z),
                AlbedoTextureI = GetTextureIndex(asset.AlbedoMap),

                Roughness = asset.Roughness,
                RoughnessTextureI = GetTextureIndex(asset.RoughnessMap),

                Metallic = asset.Metallic,
                MetallicTextureI = GetTextureIndex(asset.MetallicMap),

                Emissive = asset.Emissive,
                EmissiveTextureI = GetTextureIndex(asset.EmissiveMap),

                Alpha = asset.Albedo.W,
                AlphaCutoff = asset.AlphaCutoff,
                Opaque = asset.Opaque ? 1u : 0u
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
            var uploadBuffer = Ctx.CreateBuffer(
                asset.Size.X * asset.Size.Y * asset.Format.Size(),
                BufferUsageFlags.TransferSrcBit,
                MemoryUsage.CPU_Only
            );

            var pixels = uploadBuffer.Map<byte>();
            asset.Write(pixels);
            uploadBuffer.Unmap();

            var image = Ctx.CreateImage(
                asset.Size,
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                asset.Format switch {
                    TextureFormat.R => Format.R8Unorm,
                    TextureFormat.Rgba => Format.R8G8B8A8Unorm,
                    _ => throw new ArgumentOutOfRangeException()
                }
            );

            Ctx.Run(commandBuffer => {
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

        public Vector3 CameraPos;

        public uint LightCount;
    }

    private enum GpuLightType : uint {
        Point,
        Directional
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuLight {
        public Vector3 Color;
        public GpuLightType Type;

        public Vector3 Data;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuMaterial {
        public Vector3 Albedo;
        public uint AlbedoTextureI;

        public float Roughness;
        public uint RoughnessTextureI;

        public float Metallic;
        public uint MetallicTextureI;

        public Vector3 Emissive;
        public uint EmissiveTextureI;

        public float Alpha;
        public float AlphaCutoff;
        public uint Opaque;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private uint _0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawData {
        public Matrix4x4 PositionMatrix;
        public Matrix3X4<float> NormalMatrix;

        public uint MaterialI;
    }
}