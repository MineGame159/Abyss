using System.Numerics;

namespace Abyss.Engine.Assets.Gltf;

public class GltfPbrSpecularGlossinessExt : IGltfExt {
    public static string Name => "KHR_materials_pbrSpecularGlossiness";

    public Vector4 DiffuseFactor = Vector4.One;
    public GltfTextureInfo? DiffuseTexture = null;

    public Vector3 SpecularFactor = Vector3.One;
    public float GlossinessFactor = 1;
    public GltfTextureInfo? GlossinessSpecularTexture = null;
}