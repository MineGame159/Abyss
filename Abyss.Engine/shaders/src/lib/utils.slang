static const let gamma = 2.2;
static const let invGamma = 1.0 / gamma;

float3 ToLinear(float3 color) {
    return pow(color, gamma);
}

float3 ApplyGamma(float3 linear) {
    return pow(linear, invGamma);
}
