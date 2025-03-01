struct VertexOut {
    float4 pos : SV_Position;
    float2 uv;
}

[shader("vertex")]
VertexOut vertex(uint index : SV_VertexID) {
    VertexOut out;

    out.uv = float2((index << 1) & 2, index & 2);
    out.pos = float4(out.uv * 2.0 + -1.0, 0.0, 1.0);

    return out;
}
