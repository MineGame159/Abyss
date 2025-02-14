import "lib/tonemap";

struct FrameUniforms {
    float4x4 projection;
    float4x4 view;
    float4x4 projectionView;

    float3 cameraPos;

    WorldEnvironment env;
}

struct WorldEnvironment {
    float3 sunColor;
    float3 sunDirection;

    float ambientStrength;
}

struct Material {
    float4 albedo;
    uint albedoTextureI;
}

struct DrawData {
    float4x4 positionMatrix;
    float3x3 normalMatrix;

    uint materialI;
}

[vk_binding(0, 0)]
uniform ConstantBuffer<FrameUniforms> frame;

[vk_binding(1, 0)]
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

static const let gamma = 2.2;
static const let shininess = 0.5;

[shader("fragment")]
float4 fragment(float3 pos, float2 uv, float3 normal) {
    let material = materials[draw.materialI];

    // Albedo

    var albedo = material.albedo;

    if (material.albedoTextureI != 0) {
        let sample = textures[material.albedoTextureI - 1].Sample(uv);

        albedo.rgb *= pow(sample.rgb, gamma);
        albedo.a *= sample.a;
    }

    if (albedo.a <= 0)
        discard;

    // Lighting

    let env = frame.env;

    let ambient = env.ambientStrength * env.sunColor;

    let diff = max(dot(normal, env.sunDirection), 0.0);
    let diffuse = diff * env.sunColor;

    let viewDir = normalize(frame.cameraPos - pos);
    let halfwayDir = normalize(env.sunDirection + viewDir);
    let spec = pow(max(dot(normal, halfwayDir), 0.0), shininess);
    let specular = env.sunColor * spec;

    let result = (ambient + diffuse + specular) * albedo.rgb;

    return float4(Uncharted2Filmic(result), albedo.a);
}
