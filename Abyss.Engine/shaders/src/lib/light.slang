enum LightType : uint {
    Point,
    Directional
}

struct Light {
    float3 color;
    LightType type;

    float3 data;

    float3 GetRadiance(float3 pos, out float3 lightDir) {
        switch (type) {
        case LightType::Point:
            return Point_GetRadiance(pos, lightDir);
        case LightType::Directional:
            return Directional_GetRadiance(pos, lightDir);
        default:
            lightDir = 0.0;
            return 0.0;
        }
    }

    float3 Point_GetRadiance(float3 pos, out float3 lightDir) {
        let distance = length(this.data - pos);
        let attenuation = 1.0 / (distance * distance);
        let radiance = this.color * attenuation;

        lightDir = normalize(this.data - pos);
        return radiance;
    }

    float3 Directional_GetRadiance(float3 pos, out float3 lightDir) {
        lightDir = this.data;
        return this.color;
    }
}
