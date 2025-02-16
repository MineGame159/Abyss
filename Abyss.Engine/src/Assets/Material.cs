using System.Numerics;
using Abyss.Engine.Gui;

namespace Abyss.Engine.Assets;

[Inspectable]
public class Material {
    // Albedo

    public ITexture? AlbedoMap;

    [InspectorFloat(0.005f, 0)]
    public Vector4 Albedo;

    // Roughness

    public ITexture? RoughnessMap;

    [InspectorFloat(0.005f, 0, 1)]
    public float Roughness;

    // Metallic

    public ITexture? MetallicMap;

    [InspectorFloat(0.005f, 0, 1)]
    public float Metallic;
}