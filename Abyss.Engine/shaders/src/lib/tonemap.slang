float Luminance(float3 v) {
    return dot(v, float3(0.2126, 0.7152, 0.0722));
}

float3 Reinhard(float3 v) {
    return v / (1.0f + v);
}

float3 ReinhardExtended(float3 v, float maxWhite) {
    let numerator = v * (1.0 + (v / float3(maxWhite * maxWhite)));
    return numerator / (1.0 + v);
}

float3 ReinhardJodie(float3 v) {
    let l = Luminance(v);
    let tv = v / (1.0 + v);
    return lerp(v / (1.0 + l), tv, tv);
}

float3 Uncharted2TonemapPartial(float3 x) {
    let A = 0.15;
    let B = 0.50;
    let C = 0.10;
    let D = 0.20;
    let E = 0.02;
    let F = 0.30;
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

float3 Uncharted2Filmic(float3 v) {
    let exposure_bias = 2.0;
    let curr = Uncharted2TonemapPartial(v * exposure_bias);

    let W = float3(11.2);
    let white_scale = float3(1.0) / Uncharted2TonemapPartial(W);
    return curr * white_scale;
}

float3 AcesApprox(float3 v) {
    v *= 0.6;
    let a = 2.51;
    let b = 0.03;
    let c = 2.43;
    let d = 0.59;
    let e = 0.14;
    return clamp((v * (a * v + b)) / (v * (c * v + d) + e), 0.0, 1.0);
}