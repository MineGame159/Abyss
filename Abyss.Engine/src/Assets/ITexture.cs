using Silk.NET.Maths;

namespace Abyss.Engine.Assets;

public enum TextureFormat {
    R,
    Rgba
}

public interface ITexture {
    TextureFormat Format { get; }
    Vector2D<uint> Size { get; }

    void Write(Span<byte> pixels);
}

public static class TextureFormatExt {
    public static uint Size(this TextureFormat format) {
        return format switch {
            TextureFormat.R => 1,
            TextureFormat.Rgba => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }
}