using System.Numerics;

namespace Abyss.Engine.Assets;

public record struct Vertex(Vector3 Pos, Vector2 Uv);

public interface IMesh {
    uint? IndexCount { get; }
    
    uint VertexCount { get; }

    void WriteIndices(Span<uint> indices);
    
    void WriteVertices(Span<Vertex> vertices);
}