using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Abyss.Engine.Render;

[StructLayout(LayoutKind.Sequential)]
public struct AlignedVector3 {
    public float X, Y, Z;

    public AlignedVector3(float x, float y, float z) {
        X = x;
        Y = y;
        Z = z;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private float _0;

    public static implicit operator Vector3(AlignedVector3 v) => new(v.X, v.Y, v.Z);
    public static implicit operator AlignedVector3(Vector3 v) => new(v.X, v.Y, v.Z);
}