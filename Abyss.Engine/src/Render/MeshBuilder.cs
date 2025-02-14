using System.Numerics;
using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Engine.Assets;
using Abyss.Gpu;
using Silk.NET.Vulkan;
using VMASharp;

namespace Abyss.Engine.Render;

[StructLayout(LayoutKind.Sequential)]
internal record struct Vertex {
    public Vector3 Pos;
    public Vector2 Uv;
    public Vector3 Normal;

    public Vertex(Vector3 pos, Vector2 uv, Vector3 normal) {
        Pos = pos;
        Uv = uv;
        Normal = normal;
    }
}

internal static class MeshBuilder {
    public static Mesh Create(GpuContext ctx, IMesh asset) {
        var mesh = new Mesh();

        // Build upload buffers

        var vertexUploadBuffer = ctx.CreateBuffer(
            asset.VertexCount * Utils.SizeOf<Vertex>(),
            BufferUsageFlags.TransferSrcBit,
            MemoryUsage.CPU_Only
        );

        var indexUploadBuffer = default(GpuBuffer?);

        var vertices = vertexUploadBuffer.Map<Vertex>();
        var calculateNormals = WriteVertices(asset, vertices);

        if (asset.IndexCount != null) {
            indexUploadBuffer = ctx.CreateBuffer(
                asset.IndexCount!.Value * Utils.SizeOf<uint>(),
                BufferUsageFlags.TransferSrcBit,
                MemoryUsage.CPU_Only
            );

            var indices = indexUploadBuffer.Map<uint>();
            asset.WriteIndices(indices);

            if (calculateNormals)
                CalculateNormals(indices, vertices);

            indexUploadBuffer.Unmap();
        }
        else {
            if (calculateNormals)
                CalculateNormals(vertices);
        }

        vertexUploadBuffer.Unmap();

        // Copy to actual buffers

        mesh.VertexBuffer = ctx.CreateBuffer(
            vertexUploadBuffer.Size,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.GPU_Only
        );

        if (indexUploadBuffer != null) {
            mesh.IndexBuffer = ctx.CreateBuffer(
                indexUploadBuffer.Size,
                BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryUsage.GPU_Only
            );
        }

        ctx.Run(commandBuffer => {
            // ReSharper disable AccessToDisposedClosure
            commandBuffer.CopyBuffer(vertexUploadBuffer, mesh.VertexBuffer);

            if (indexUploadBuffer != null)
                commandBuffer.CopyBuffer(indexUploadBuffer, mesh.IndexBuffer!);
            // ReSharper restore AccessToDisposedClosure
        });

        vertexUploadBuffer.Dispose();
        indexUploadBuffer?.Dispose();

        return mesh;
    }

    private static bool WriteVertices(IMesh asset, Span<Vertex> vertices) {
        using var positions = asset.VertexPositions().GetEnumerator();
        using var uvs = asset.VertexUvs().GetEnumerator();

        if (asset.VertexNormals() != null) {
            using var normals = asset.VertexNormals()!.GetEnumerator();

            for (var i = 0; i < asset.VertexCount; i++) {
                positions.MoveNext();
                uvs.MoveNext();
                normals.MoveNext();

                vertices[i] = new Vertex(positions.Current, uvs.Current, normals.Current);
            }

            return false;
        }

        for (var i = 0; i < asset.VertexCount; i++) {
            positions.MoveNext();
            uvs.MoveNext();

            vertices[i] = new Vertex(positions.Current, uvs.Current, Vector3.Zero);
        }

        return true;
    }

    private static void CalculateNormals(Span<uint> indices, Span<Vertex> vertices) {
        for (var i = 0; i < indices.Length / 3; i += 3) {
            ref var v0 = ref vertices[(int) indices[i + 0]];
            ref var v1 = ref vertices[(int) indices[i + 1]];
            ref var v2 = ref vertices[(int) indices[i + 2]];

            var normal = Vector3.Cross(v1.Pos - v0.Pos, v2.Pos - v0.Pos);

            v0.Normal += normal;
            v1.Normal += normal;
            v2.Normal += normal;
        }

        foreach (ref var v in vertices) {
            v.Normal = Vector3.Normalize(v.Normal);
        }
    }

    private static void CalculateNormals(Span<Vertex> vertices) {
        for (var i = 0; i < vertices.Length / 3; i += 3) {
            ref var v0 = ref vertices[i + 0];
            ref var v1 = ref vertices[i + 1];
            ref var v2 = ref vertices[i + 2];

            var normal = Vector3.Normalize(Vector3.Cross(v1.Pos - v0.Pos, v2.Pos - v0.Pos));

            v0.Normal = normal;
            v1.Normal = normal;
            v2.Normal = normal;
        }
    }
}

internal class Mesh {
    public GpuBuffer? IndexBuffer;
    public GpuBuffer VertexBuffer = null!;
}