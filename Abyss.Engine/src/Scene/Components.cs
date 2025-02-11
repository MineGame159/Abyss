using System.Numerics;
using Abyss.Engine.Assets;

namespace Abyss.Engine.Scene;

public struct Transform {
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale) {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

    public Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public void Apply(Transform other) {
        var combined = other.Matrix * Matrix;
        Matrix4x4.Decompose(combined, out Scale, out Rotation, out Position);

        //Rotation *= other.Rotation;
        //Scale = Vector3.Transform(Scale * Vector3.Transform(other.Scale, other.Rotation), Quaternion.Inverse(other.Rotation));
        //Position += Vector3.Transform(other.Position * Scale, Rotation);
    }
}

public record struct Camera(float Fov, float Near, float Far);

public record struct MeshInstance(IMesh Mesh, Material Material);