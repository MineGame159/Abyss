using System.Numerics;

namespace Abyss.Core;

public record struct Rgba(byte R, byte G, byte B, byte A) {
    public static Rgba From(Vector4 color) => new(
        (byte) (Math.Clamp(color.X, 0, 1) * 255),
        (byte) (Math.Clamp(color.Y, 0, 1) * 255),
        (byte) (Math.Clamp(color.Z, 0, 1) * 255),
        (byte) (Math.Clamp(color.W, 0, 1) * 255)
    );

    public static Rgba From(float[] color) => new(
        (byte) (Math.Clamp(color[0], 0, 1) * 255),
        (byte) (Math.Clamp(color[1], 0, 1) * 255),
        (byte) (Math.Clamp(color[2], 0, 1) * 255),
        (byte) (Math.Clamp(color[3], 0, 1) * 255)
    );

    public Vector4 Float => new(R / 255f, G / 255f, B / 255f, A / 255f);
}