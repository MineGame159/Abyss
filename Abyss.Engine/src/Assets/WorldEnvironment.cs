using System.Numerics;
using Abyss.Engine.Gui;

namespace Abyss.Engine.Assets;

[Inspectable]
public class WorldEnvironment {
    [InspectorFloat(0.001f, 0, 1)]
    public Vector3 ClearColor = Vector3.One;
}