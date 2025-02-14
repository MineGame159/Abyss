using System.Numerics;

namespace Abyss.Engine.Assets;

public class WorldEnvironment {
    public Vector3 ClearColor = Vector3.One;

    public float AmbientStrength = 0.25f;

    public Vector3 SunColor = Vector3.One;
    public Vector3 SunDirection = Vector3.UnitY;
}