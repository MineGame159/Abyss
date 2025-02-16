import "lib/light";
import "lib/pbr";
import "lib/tonemap";
import "lib/utils";

struct FrameUniforms {
    float4x4 projection;
    float4x4 view;
    float4x4 projectionView;

    float3 cameraPos;

    uint lightCount;
}

struct Material {
    float3 albedo;
    uint albedoTextureI;

    float roughness;
    uint roughnessTextureI;

    float metallic;
    uint metallicTextureI;

    float alpha;
}

struct DrawData {
    float4x4 positionMatrix;
    float3x3 normalMatrix;

    uint materialI;
}

[vk_binding(0, 0)]
uniform ConstantBuffer<FrameUniforms> frame;

[vk_binding(1, 0)]
uniform StructuredBuffer<Light> lights;

[vk_binding(2, 0)]
uniform StructuredBuffer<Material> materials;

[vk_binding(0, 1)]
uniform Sampler2D[] textures;

[vk_push_constant]
DrawData draw;

struct VertexIn {
    float3 pos;
    float2 uv;
    float3 normal;
}

struct VertexOut {
    float4 svPos : SV_Position;

    float3 pos;
    float2 uv;
    float3 normal;
}

[shader("vertex")]
VertexOut vertex(VertexIn in) {
    VertexOut out;

    out.svPos = mul(mul(frame.projectionView, draw.positionMatrix), float4(in.pos, 1.0));
    out.pos = mul(draw.positionMatrix, float4(in.pos, 1.0)).xyz;
    out.uv = in.uv;
    out.normal = normalize(mul(draw.normalMatrix, in.normal));

    return out;
}

[shader("fragment")]
float4 fragment(float3 pos, float2 uv, float3 normal) {
    // Material

    let material = materials[draw.materialI];

    var alpha = 1.0;
    let pbrMat = GetPbrMaterial(uv, material, alpha);

    // Lighting

    let N = normalize(normal);
    let V = normalize(frame.cameraPos - pos);

    var Lo = float3(0.0);

    for (uint i = 0; i < frame.lightCount; i++) {
        let light = lights[i];

        var L = float3(0.0);
        let radiance = light.GetRadiance(pos, L);

        Lo += EvaluateBrdf(N, V, L, radiance, pbrMat);
    }

    // Result

    let ambient = float3(0.03) * pbrMat.albedo;

    var color = ambient + Lo;
    color = Uncharted2Filmic(color);

    return float4(color, alpha);
}

PbrMaterial GetPbrMaterial(float2 uv, Material material, out float alpha) {
    PbrMaterial pbrMat;

    // Albedo & Alpha

    pbrMat.albedo = material.albedo;
    alpha = material.alpha;

    if (material.albedoTextureI != 0) {
        let sample = textures[material.albedoTextureI - 1].Sample(uv);

        pbrMat.albedo *= ToLinear(sample.rgb);
        alpha *= sample.a;
    }

    if (alpha <= 0)
        discard;

    // Roughness

    pbrMat.roughness = material.roughness;

    if (material.roughnessTextureI != 0) {
        pbrMat.roughness *= textures[material.roughnessTextureI - 1].Sample(uv).r;
    }

    // Metallic

    pbrMat.metallic = material.metallic;

    if (material.metallicTextureI != 0) {
        pbrMat.metallic *= textures[material.metallicTextureI - 1].Sample(uv).r;
    }

    return pbrMat;
}
