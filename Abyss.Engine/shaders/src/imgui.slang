struct VertexIn {
    float2 pos;
    float2 uv;
    float4 color;
}

struct VertexOut {
    float4 pos : SV_Position;
    float2 uv;
    float4 color;
}

struct DrawData {
    float2 scale;
    float2 translate;
};

[vk_push_constant]
DrawData draw;

[vk_binding(0, 0)]
uniform Sampler2D texture;

[shader("vertex")]
VertexOut vertex(VertexIn in) {
    VertexOut out;

    out.pos = float4(in.pos * draw.scale + draw.translate, 1.0);
    out.uv = in.uv;
    out.color = in.color;

    return out;
}

static const let gamma = 2.2;

[shader("fragment")]
float4 fragment(VertexOut in) {
    let color = texture.Sample(in.uv) * in.color;
    return float4(pow(color.rgb, gamma), color.a);
}
