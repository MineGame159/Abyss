using System.Numerics;
using Abyss.Engine.Gui;

namespace Abyss.Engine.Assets;

[Inspectable]
public class WorldEnvironment {
    [InspectorFloat(0.001f, 0, 1)]
    public Vector3 ClearColor = Vector3.One;

    // Bloom

    public bool Bloom = true;

    [InspectorFloat(0.001f, 0)]
    public float BloomThreshold = 0.95f;
}