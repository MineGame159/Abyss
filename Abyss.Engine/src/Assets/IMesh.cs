using System.Numerics;

namespace Abyss.Engine.Assets;

public interface IMesh {
    uint? IndexCount { get; }

    uint VertexCount { get; }

    void WriteIndices(Span<uint> indices);

    IEnumerable<Vector3> VertexPositions();

    IEnumerable<Vector2> VertexUvs();

    IEnumerable<Vector3>? VertexNormals();
}