using Abyss.Core;
using Silk.NET.Maths;

namespace Abyss.Engine.Assets;

public interface ITexture {
    Vector2D<uint> Size { get; }

    void Write(Span<Rgba> pixels);
}