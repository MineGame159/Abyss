struct PushConstants {
    float radius;
}

[vk_binding(0, 0)]
uniform Sampler2D srcTexture;

[vk_push_constant]
ConstantBuffer<PushConstants> pc;

[shader("fragment")]
float3 fragment(float2 uv) {
    // The filter kernel is applied with a radius, specified in texture
    // coordinates, so that the radius will vary across mip resolutions.
    let x = pc.radius;
    let y = pc.radius;

    // Take 9 samples around current texel:
    // a - b - c
    // d - e - f
    // g - h - i
    // === ('e' is the current texel) ===
    let a = srcTexture.Sample(float2(uv.x - x, uv.y + y)).rgb;
    let b = srcTexture.Sample(float2(uv.x, uv.y + y)).rgb;
    let c = srcTexture.Sample(float2(uv.x + x, uv.y + y)).rgb;

    let d = srcTexture.Sample(float2(uv.x - x, uv.y)).rgb;
    let e = srcTexture.Sample(float2(uv.x, uv.y)).rgb;
    let f = srcTexture.Sample(float2(uv.x + x, uv.y)).rgb;

    let g = srcTexture.Sample(float2(uv.x - x, uv.y - y)).rgb;
    let h = srcTexture.Sample(float2(uv.x, uv.y - y)).rgb;
    let i = srcTexture.Sample(float2(uv.x + x, uv.y - y)).rgb;

    // Apply weighted distribution, by using a 3x3 tent filter:
    //  1   | 1 2 1 |
    // -- * | 2 4 2 |
    // 16   | 1 2 1 |
    var upsample = e * 4.0;
    upsample += (b + d + f + h) * 2.0;
    upsample += (a + c + g + i);
    upsample *= 1.0 / 16.0;

    return upsample;
}