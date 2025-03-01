using System.Numerics;
using Abyss.Engine.Assets;
using Abyss.Engine.Gui;
using Arch.Core;

namespace Abyss.Engine.Scene;

public record struct Root;

public record struct Info {
    public string Name;
    public bool Visible;

    public Entity? Parent;
    public List<Entity>? Children;

    public Info(string name, bool visible = true) {
        Name = name;
        Visible = visible;
    }

    public Info() : this("") { }
}

public record struct Transform {
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale) {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public Transform(Matrix4x4 matrix) {
        if (!Matrix4x4.Decompose(matrix, out Scale, out Rotation, out Position))
            throw new Exception("Failed to create a Transform from a Matrix4x4");
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

public record struct Camera {
    [InspectorFloat(0.1f, 0)]
    public float Fov;
    
    [InspectorFloat(0.1f, 0)]
    public float Near;
    
    [InspectorFloat(0.1f, 0)]
    public float Far;

    public WorldEnvironment Environment;

    public Camera(float fov, float near, float far, WorldEnvironment environment) {
        Fov = fov;
        Near = near;
        Far = far;
        Environment = environment;
    }
}

public record struct MeshInstance {
    public IMesh Mesh;
    public Material Material;

    public MeshInstance(IMesh mesh, Material material) {
        Mesh = mesh;
        Material = material;
    }
}

public record struct PointLight {
    [InspectorFloat(0.005f, 0)]
    public Vector3 Color;

    [InspectorFloat(0.005f, 0)]
    public float Intensity;

    public PointLight(Vector3 color, float intensity) {
        Color = color;
        Intensity = intensity;
    }
}

public record struct DirectionalLight {
    [InspectorFloat(0.05f)]
    public Vector3 Direction;

    [InspectorFloat(0.005f, 0)]
    public Vector3 Color;

    [InspectorFloat(0.005f, 0)]
    public float Intensity;

    public DirectionalLight(Vector3 direction, Vector3 color, float intensity) {
        Direction = direction;
        Color = color;
        Intensity = intensity;
    }
}