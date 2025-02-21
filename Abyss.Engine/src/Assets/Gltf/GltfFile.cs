using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abyss.Core;

namespace Abyss.Engine.Assets.Gltf;

[JsonSourceGenerationOptions(
    IncludeFields = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    Converters = [
        typeof(Matrix4X4Converter),
        typeof(QuaternionConverter),
        typeof(Vector4Converter),
        typeof(Vector3Converter)
    ]
)]
[JsonSerializable(typeof(GltfFile))]
[JsonSerializable(typeof(GltfLightsPunctualExt))]
[JsonSerializable(typeof(GltfPbrSpecularGlossinessExt))]
public partial class GltfJsonContext : JsonSerializerContext;

public class GltfFile {
    public GltfAccessor[] Accessors = [];
    public GltfBuffer[] Buffers = [];
    public GltfBufferView[] BufferViews = [];
    public GltfCamera[] Cameras = [];
    public GltfImage[] Images = [];
    public GltfMaterial[] Materials = [];
    public GltfMesh[] Meshes = [];
    public GltfNode[] Nodes = [];
    public GltfSampler[] Samplers = [];
    public int? Scene = null;
    public GltfScene[] Scenes = [];
    public GltfTexture[] Textures = [];

    [JsonConverter(typeof(GltfExtensionsConverter))]
    public Dictionary<string, object> Extensions = [];

    public bool TryGetExtension<T>(out T ext) where T : IGltfExt {
        if (Extensions.TryGetValue(T.Name, out var extension)) {
            ext = (T) extension;
            return true;
        }

        ext = default!;
        return false;
    }

    public static GltfFile? Load(string path) {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    public static GltfFile? Load(Stream stream) {
        var header = stream.Read<GlbHeader>();
        stream.Seek(0, SeekOrigin.Begin);

        var length = (uint) (stream.Length - stream.Position);

        if (header.Magic == 0x46546C67) {
            length = SeekToGlbChunk(stream, GlbChunkType.Json);
        }

        var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes((int) length);

        var file = JsonSerializer.Deserialize<GltfFile>(bytes, GltfJsonContext.Default.GltfFile);

        if (file != null) {
            SetFile(file.Accessors, file);
            SetFile(file.BufferViews, file);
            SetFile(file.Images, file);
            SetFile(file.Nodes, file);
            SetFile(file.Scenes, file);
            SetFile(file.Textures, file);

            if (file.TryGetExtension(out GltfLightsPunctualExt lights)) {
                SetFile(lights, file);
            }

            foreach (var node in file.Nodes) {
                if (node.TryGetExtension(out GltfLightsPunctualExt light)) {
                    SetFile(light, file);
                }
            }

            foreach (var mesh in file.Meshes) {
                SetFile(mesh.Primitives, file);
            }

            foreach (var material in file.Materials) {
                SetFile(material.PbrMetallicRoughness?.BaseColorTexture, file);
                SetFile(material.PbrMetallicRoughness?.MetallicRoughnessTexture, file);
                SetFile(material.NormalTexture, file);
                SetFile(material.OcclusionTexture, file);
                SetFile(material.EmissiveTexture, file);

                if (material.TryGetExtension(out GltfPbrSpecularGlossinessExt pbrSpecular)) {
                    SetFile(pbrSpecular.DiffuseTexture, file);
                    SetFile(pbrSpecular.GlossinessSpecularTexture, file);
                }
            }
        }

        return file;
    }

    private static void SetFile<T>(T? item, GltfFile file) where T : GltfItem {
        if (item != null) {
            item.File = file;
        }
    }

    private static void SetFile<T>(T[] items, GltfFile file) where T : GltfItem {
        foreach (var item in items) {
            item.File = file;
        }
    }

    public static uint SeekToGlbChunk(Stream stream, GlbChunkType type) {
        stream.Seek(0, SeekOrigin.Begin);
        stream.Read<GlbHeader>();

        while (true) {
            var info = stream.Read<GlbChunkInfo>();

            if (info.Type == type) {
                return info.Length;
            }

            stream.Seek(info.Length, SeekOrigin.Current);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
file struct GlbHeader {
    public uint Magic;
    public uint Version;
    public uint Length;
}

[StructLayout(LayoutKind.Sequential)]
file struct GlbChunkInfo {
    public uint Length;
    public GlbChunkType Type;
}

public enum GlbChunkType : uint {
    Json = 0x4E4F534A,
    Bin = 0x004E4942
}

public abstract class GltfItem {
    [JsonIgnore] public GltfFile File = null!;
}

public class GltfAccessor : GltfItem {
    [JsonPropertyName("bufferView")] public int? BufferViewIndex = null;
    public uint ByteOffset = 0;
    public required GltfComponentType ComponentType;
    public bool Normalized = false;
    public required uint Count;
    public required GltfType Type;

    [JsonIgnore] public GltfBufferView? BufferView => BufferViewIndex != null ? File.BufferViews[BufferViewIndex!.Value] : null;
}

public class GltfBuffer {
    public string? Uri = null;
    public required uint ByteLength;
}

public class GltfBufferView : GltfItem {
    [JsonPropertyName("buffer")] public required int BufferIndex;
    public uint ByteOffset = 0;
    public required uint ByteLength;
    public uint? ByteStride = null;

    [JsonIgnore] public GltfBuffer Buffer => File.Buffers[BufferIndex];
}

public class GltfCamera {
    public GltfCameraOrthographic? Orthographic = null;
    public GltfCameraPerspective? Perspective = null;
    public required GltfCameraType Type;
}

public class GltfCameraOrthographic {
    [JsonPropertyName("xmag")] public required float XMag;
    [JsonPropertyName("ymag")] public required float YMag;
    [JsonPropertyName("zfar")] public required float ZFar;
    [JsonPropertyName("znear")] public required float ZNear;
}

public class GltfCameraPerspective {
    public float? AspectRatio = null;
    [JsonPropertyName("yfov")] public required float YFov;
    [JsonPropertyName("zfar")] public float? ZFar = null;
    [JsonPropertyName("znear")] public required float ZNear;
}

public class GltfImage : GltfItem {
    public string? Uri = null;
    public string? MimeType = null;
    [JsonPropertyName("bufferView")] public int? BufferViewIndex = null;

    [JsonIgnore] public GltfBufferView? BufferView => BufferViewIndex != null ? File.BufferViews[BufferViewIndex!.Value] : null;
}

public class GltfMaterial {
    public GltfPbrMetallicRoughness? PbrMetallicRoughness = null;
    public GltfNormalTextureInfo? NormalTexture = null;
    public GltfOcclusionTextureInfo? OcclusionTexture = null;
    public GltfTextureInfo? EmissiveTexture = null;
    public Vector3 EmissiveFactor = Vector3.Zero;
    public GltfAlphaMode AlphaMode = GltfAlphaMode.Opaque;
    public float AlphaCutoff = 0.5f;
    public bool DoubleSided = false;

    [JsonConverter(typeof(GltfExtensionsConverter))]
    public Dictionary<string, object> Extensions = [];

    public bool TryGetExtension<T>(out T ext) where T : IGltfExt {
        if (Extensions.TryGetValue(T.Name, out var extension)) {
            ext = (T) extension;
            return true;
        }

        ext = default!;
        return false;
    }
}

public class GltfPbrMetallicRoughness {
    public Vector4 BaseColorFactor = Vector4.One;
    public GltfTextureInfo? BaseColorTexture = null;
    public float MetallicFactor = 1;
    public float RoughnessFactor = 1;
    public GltfTextureInfo? MetallicRoughnessTexture = null;
}

public class GltfNormalTextureInfo : GltfItem {
    public required int Index;
    public int TexCoord = 0;
    public float Scale = 1;

    [JsonIgnore] public GltfTexture Texture => File.Textures[Index];
}

public class GltfOcclusionTextureInfo : GltfItem {
    public required int Index;
    public int TexCoord = 0;
    public float Strength = 1;

    [JsonIgnore] public GltfTexture Texture => File.Textures[Index];
}

public class GltfTextureInfo : GltfItem {
    public required int Index;
    public int TexCoord = 0;

    [JsonIgnore] public GltfTexture Texture => File.Textures[Index];
}

public class GltfMesh {
    public required GltfPrimitive[] Primitives;
}

public class GltfPrimitive : GltfItem {
    public required Dictionary<string, int> Attributes;
    public int? Indices = null;
    public int? Material = null;
    public GltfPrimitiveMode Mode = GltfPrimitiveMode.Triangles;

    public bool TryGetAttribute(string key, out GltfAccessor accessor) {
        if (Attributes.TryGetValue(key, out var index)) {
            accessor = File.Accessors[index];
            return true;
        }

        accessor = null!;
        return false;
    }
}

public class GltfNode : GltfItem {
    [JsonPropertyName("camera")] public int? CameraIndex = null;
    public int[]? Children = null;
    public Matrix4x4 Matrix = Matrix4x4.Identity;
    [JsonPropertyName("mesh")] public int? MeshIndex = null;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;
    public Vector3 Translation = Vector3.Zero;
    public string? Name = null;

    [JsonConverter(typeof(GltfExtensionsConverter))]
    public Dictionary<string, object> Extensions = [];

    public bool TryGetExtension<T>(out T ext) where T : IGltfExt {
        if (Extensions.TryGetValue(T.Name, out var extension)) {
            ext = (T) extension;
            return true;
        }

        ext = default!;
        return false;
    }

    [JsonIgnore] public GltfCamera? Camera => CameraIndex != null ? File.Cameras[CameraIndex!.Value] : null;
    [JsonIgnore] public GltfMesh? Mesh => MeshIndex != null ? File.Meshes[MeshIndex!.Value] : null;

    public IEnumerator<GltfNode> GetEnumerator() {
        if (Children != null) {
            foreach (var index in Children) {
                yield return File.Nodes[index];
            }
        }
    }
}

public class GltfSampler {
    public GltfFilter? MagFilter = null;
    public GltfFilter? MinFilter = null;
    public GltfWrap WrapS = GltfWrap.Repeat;
    public GltfWrap WrapT = GltfWrap.Repeat;
}

public class GltfScene : GltfItem {
    public int[]? Nodes = null;

    public IEnumerator<GltfNode> GetEnumerator() {
        if (Nodes != null) {
            foreach (var index in Nodes) {
                yield return File.Nodes[index];
            }
        }
    }
}

public class GltfTexture : GltfItem {
    [JsonPropertyName("sampler")] public int? SamplerIndex = null;
    [JsonPropertyName("source")] public int? SourceIndex = null;

    [JsonIgnore] public GltfSampler? Sampler => SamplerIndex != null ? File.Samplers[SamplerIndex!.Value] : null;
    [JsonIgnore] public GltfImage? Image => SourceIndex != null ? File.Images[SourceIndex!.Value] : null;
}

public enum GltfComponentType {
    Byte = 5120,
    UnsignedByte = 5121,
    Short = 5122,
    UnsignedShort = 5123,
    UnsignedInt = 5125,
    Float = 5126
}

[JsonConverter(typeof(JsonStringEnumConverter<GltfType>))]
public enum GltfType {
    [JsonStringEnumMemberName("SCALAR")] Scalar,
    [JsonStringEnumMemberName("VEC2")] Vec2,
    [JsonStringEnumMemberName("VEC3")] Vec3,
    [JsonStringEnumMemberName("VEC4")] Vec4,
    [JsonStringEnumMemberName("MAT2")] Mat2,
    [JsonStringEnumMemberName("MAT3")] Mat3,
    [JsonStringEnumMemberName("MAT4")] Mat4
}

[JsonConverter(typeof(JsonStringEnumConverter<GltfCameraType>))]
public enum GltfCameraType {
    [JsonStringEnumMemberName("perspective")]
    Perspective,

    [JsonStringEnumMemberName("orthographic")]
    Orthographic
}

[JsonConverter(typeof(JsonStringEnumConverter<GltfAlphaMode>))]
public enum GltfAlphaMode {
    [JsonStringEnumMemberName("OPAQUE")] Opaque,
    [JsonStringEnumMemberName("MASK")] Mask,
    [JsonStringEnumMemberName("BLEND")] Blend
}

public enum GltfPrimitiveMode {
    Points = 0,
    Lines = 1,
    LineLoop = 2,
    LineStrip = 3,
    Triangles = 4,
    TriangleStrip = 5,
    TriangleFan = 6
}

public enum GltfFilter {
    Nearest = 9728,
    Linear = 9729,
    NearestMipmapNearest = 9984,
    LinearMipmapNearest = 9985,
    NearestMipmapLinear = 9986,
    LinearMipmapLinear = 9987
}

public interface IGltfExt {
    static abstract string Name { get; }
}

public enum GltfWrap {
    ClampToEdge = 33071,
    MirroredRepeat = 33648,
    Repeat = 10497
}