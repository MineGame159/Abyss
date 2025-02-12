struct FrameUniforms {
    float4x4 projection;
    float4x4 view;
    float4x4 projectionView;
}

struct DrawData {
    float4x4 transform;
    float4 albedo;
    uint albedoTextureI;
}

[vk_binding(0, 0)]
uniform ConstantBuffer<FrameUniforms> frame;

[vk_binding(0, 1)]
uniform Sampler2D[] textures;

[vk_push_constant]
DrawData draw;

struct VertexIn {
    float3 pos;
    float2 uv;
}

struct VertexOut {
    float4 pos : SV_Position;
    float2 uv;
}

[shader("vertex")]
VertexOut vertex(VertexIn in) {
    VertexOut out;

    out.pos = mul(mul(frame.projectionView, draw.transform), float4(in.pos, 1.0));
    out.uv = in.uv;

    return out;
}

[shader("fragment")]
float4 fragment(float2 uv) {
    var color = draw.albedo;

    if (draw.albedoTextureI != 0)
        color *= textures[draw.albedoTextureI - 1].Sample(uv);

    if (color.a <= 0)
        discard;

    return color;
}
