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

    public static implicit operator Vector4(Rgba color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}