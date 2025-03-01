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
    
    // Emissive

    public ITexture? EmissiveMap;

    [InspectorFloat(0.005f, 0)]
    public Vector3 Emissive;

    // Alpha

    [InspectorFloat(0.005f, 0, 1)]
    public float AlphaCutoff;

    public bool Opaque;

    // Normal

    public ITexture? NormalMap;
}