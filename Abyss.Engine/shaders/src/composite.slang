import "lib/tonemap";

[vk_binding(0, 0)]
uniform Sampler2D colorTexture;

[shader("fragment")]
float3 fragment(float2 uv) {
    var color = colorTexture.Sample(uv).rgb;

    color = Uncharted2Filmic(color);

    return color;
}
