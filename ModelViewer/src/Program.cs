using System.Numerics;
using Abyss.Core;
using Abyss.Engine;
using Abyss.Engine.Assets;
using Abyss.Engine.Scene;
using Arch.Core;
using Arch.Core.Extensions;
using Silk.NET.Input;

namespace ModelViewer;

public class Program : Application {
    private Entity camera;
    private float yaw, pitch;

    protected override void Init() {
        var model = Model.Load("models/mando_helmet.glb");

        model.Spawn(World, new Transform {
            Scale = new Vector3(1)
        });

        camera = World.Spawn(
            new Transform {
                Position = new Vector3(0, 0, 0)
            },
            new Camera(75, 0.1f, 2048, new WorldEnvironment {
                ClearColor = new Vector3(0.75f)
            }),
            name: "Camera"
        );

        World.Spawn(
            new Transform {
                Position = new Vector3(5)
            },
            new DirectionalLight {
                Direction = new Vector3(1.3f, -2.9f, -2.5f),
                Color = new Vector3(1),
                Intensity = 6
            },
            name: "Light"
        );
    }

    protected override void Update(float delta) {
        ref var cameraTransform = ref camera.Get<Transform>();
        MoveCamera(delta, ref cameraTransform);
    }

    private void MoveCamera(float delta, ref Transform transform) {
        // Rotation
        if (Input.IsKeyDown(Key.Right)) yaw -= 90 * delta;
        if (Input.IsKeyDown(Key.Left)) yaw += 90 * delta;
        if (Input.IsKeyDown(Key.Up)) pitch -= 90 * delta;
        if (Input.IsKeyDown(Key.Down)) pitch += 90 * delta;

        pitch = Math.Clamp(pitch, -89.95f, 89.95f);
        transform.Rotation = Quaternion.CreateFromYawPitchRoll(Utils.DegToRad(yaw), Utils.DegToRad(pitch), 0);

        // Movement
        var speed = 20 * delta;
        if (Input.IsKeyDown(Key.ControlLeft)) speed *= 15;

        var forward = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(Utils.DegToRad(yaw), 0, 0));
        var right = Vector3.Normalize(Vector3.Cross(forward, -Vector3.UnitY));

        forward *= speed;
        right *= speed;

        if (Input.IsKeyDown(Key.W)) transform.Position += forward;
        if (Input.IsKeyDown(Key.S)) transform.Position -= forward;
        if (Input.IsKeyDown(Key.D)) transform.Position -= right;
        if (Input.IsKeyDown(Key.A)) transform.Position += right;
        if (Input.IsKeyDown(Key.Space)) transform.Position.Y += speed;
        if (Input.IsKeyDown(Key.ShiftLeft)) transform.Position.Y -= speed;
    }

    public static void Main() {
        var app = new Program();
        app.Run();
    }
}