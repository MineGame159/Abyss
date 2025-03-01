import "lib/light";
import "lib/pbr";
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

    float3 emissive;
    uint emissiveTextureI;

    float alpha;
    float alphaCutoff;
    uint opaque;

    uint normalTextureI;
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

    var emissive = float3(0.0);
    var alpha = 1.0;
    let pbrMat = GetPbrMaterial(uv, material, emissive, alpha);

    // Normal

    if (material.normalTextureI != 0) {
        normal = GetNormalFromNormalTexture(material.normalTextureI, uv, pos, normal);
    }

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
    let color = ambient + Lo + emissive;

    return float4(color, alpha);
}

PbrMaterial GetPbrMaterial(float2 uv, Material material, out float3 emissive, out float alpha) {
    PbrMaterial pbrMat;

    // Albedo & Alpha

    pbrMat.albedo = material.albedo;
    alpha = material.alpha;

    if (material.albedoTextureI != 0) {
        let sample = textures[material.albedoTextureI - 1].Sample(uv);

        pbrMat.albedo *= ToLinear(sample.rgb);
        alpha *= sample.a;
    }

    if (alpha <= material.alphaCutoff)
        discard;

    if (material.opaque == 1)
        alpha = 1;

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

    // Emissive

    emissive = material.emissive;

    if (material.emissiveTextureI != 0) {
        emissive *= ToLinear(textures[material.emissiveTextureI - 1].Sample(uv).rgb);
    }

    return pbrMat;
}

float3 GetNormalFromNormalTexture(uint textureI, float2 uv, float3 pos, float3 normal) {
    let tangentNormal = textures[textureI - 1].Sample(uv).xyz * 2.0 - 1.0;

    let Q1 = ddx(pos);
    let Q2 = ddy(pos);
    let st1 = ddx(uv);
    let st2 = ddy(uv);

    let N = normalize(normal);
    let T = normalize(Q1 * st2.y - Q2 * st1.y);
    let B = -normalize(cross(N, T));
    let TBN = float3x3(T, B, N);

    return normalize(mul(tangentNormal, TBN));
}
