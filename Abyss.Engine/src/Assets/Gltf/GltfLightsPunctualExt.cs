using System.Numerics;
using System.Text.Json.Serialization;

namespace Abyss.Engine.Assets.Gltf;

public class GltfLightsPunctualExt : GltfItem, IGltfExt {
    public static string Name => "KHR_lights_punctual";

    public GltfLight[]? Lights = null;
    [JsonPropertyName("light")] public int? LightIndex = null;

    [JsonIgnore]
    public GltfLight? Light => LightIndex != null ? ((GltfLightsPunctualExt) File.Extensions[Name]).Lights![LightIndex!.Value] : null;
}

public class GltfLight {
    public Vector3 Color = Vector3.One;
    public float Intensity = 1;
    public required GltfLightType Type;
    public float? Range = null;
    public GltfSpotLight? SpotLight;
}

public class GltfSpotLight {
    public float InnerConeAngle = 0;
    public float OuterConeAngle = MathF.PI / 4;
}

[JsonConverter(typeof(JsonStringEnumConverter<GltfLightType>))]
public enum GltfLightType {
    [JsonStringEnumMemberName("point")] Point,
    [JsonStringEnumMemberName("directional")] Directional,
    [JsonStringEnumMemberName("spot")] SpotLight
}