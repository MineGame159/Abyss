using System.Numerics;
using Abyss.Engine.Gui;

namespace Abyss.Engine.Assets;

[Inspectable]
public class WorldEnvironment {
    [InspectorFloat(0.001f, 0, 1)]
    public Vector3 ClearColor = Vector3.One;

    [InspectorFloat(0.001f, 0)]
    public float AmbientStrength = 0.25f;

    [InspectorFloat(0.001f, 0)]
    public Vector3 SunColor = Vector3.One;
    
    [InspectorFloat(0.001f, -1, 1)]
    public Vector3 SunDirection = Vector3.UnitY;
}