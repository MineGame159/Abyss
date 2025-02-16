using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Engine.Scene;
using glTFLoader;
using glTFLoader.Schema;
using Newtonsoft.Json.Linq;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using Buffer = glTFLoader.Schema.Buffer;
using GltfMaterial = glTFLoader.Schema.Material;
using GltfTexture = glTFLoader.Schema.Texture;
using GltfImage = glTFLoader.Schema.Image;
using Image = SixLabors.ImageSharp.Image;

namespace Abyss.Engine.Assets;

internal class GltfLoader {
    private readonly Gltf gltf;
    private readonly string gltfPath;

    private readonly Model model = new();

    private readonly Dictionary<(int?, int, int, int?), IMesh> meshes = [];

    private Material? defaultMaterial;
    private readonly Dictionary<GltfMaterial, Material> materials = [];

    private readonly Dictionary<GltfTexture, ITexture> rgbaTextures = [];
    private readonly Dictionary<GltfTexture, ITexture> roughnessTextures = [];
    private readonly Dictionary<GltfTexture, ITexture> metallicTextures = [];

    public GltfLoader(Gltf gltf, string gltfPath) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;
    }

    private void Load(Node node, Transform transform) {
        var localTransform = GetLocalTransform(node);
        transform.Apply(localTransform);

        if (node.Mesh != null) {
            foreach (var primitive in gltf.Meshes[node.Mesh!.Value].Primitives) {
                if (primitive.Mode != MeshPrimitive.ModeEnum.TRIANGLES)
                    continue;

                var posI = primitive.Attributes.GetValueOrDefault("POSITION", -1);
                if (posI == -1) continue;

                var texCoordI = primitive.Attributes.GetValueOrDefault("TEXCOORD_0", -1);
                if (texCoordI == -1) continue;

                var normalI = primitive.Attributes.GetValueOrDefault("NORMAL", -1);

                var mesh = GetMesh(primitive.Indices, posI, texCoordI, normalI);
                var material = GetMaterial(primitive.Material);

                model.Entities.Add((node.Name, transform, new MeshInstance(mesh, material), null, null));
            }
        }
        else if (node.Extensions != null && node.Extensions.TryGetValue("KHR_lights_punctual", out var extension)) {
            var lightI = ((JObject) extension)["light"].Value<int>();
            var light = (JObject) ((JArray) ((JObject) gltf.Extensions["KHR_lights_punctual"])["lights"])[lightI];
            var type = light["type"].Value<string>();

            switch (type) {
                case "point": {
                    var color = new Vector3(1);
                    var intensity = 1.0f;

                    if (light.TryGetValue("color", out var colorJ))
                        color = new Vector3(colorJ.Values<float>().ToArray());

                    if (light.TryGetValue("intensity", out var intensityJ))
                        intensity = intensityJ.Value<float>() / 1000;

                    model.Entities.Add((node.Name, transform, null, new PointLight(color, intensity), null));
                    break;
                }
                case "directional": {
                    var color = new Vector3(1);
                    var intensity = 1.0f;

                    if (light.TryGetValue("color", out var colorJ))
                        color = new Vector3(colorJ.Values<float>().ToArray());

                    if (light.TryGetValue("intensity", out var intensityJ))
                        intensity = intensityJ.Value<float>() / 1000;

                    var direction = Vector3.Transform(new Vector3(0, 0, -1), transform.Matrix);

                    model.Entities.Add((node.Name, transform, null, null, new DirectionalLight(direction, color, intensity)));
                    break;
                }
            }
        }

        if (node.Children != null) {
            foreach (var childI in node.Children) {
                Load(gltf.Nodes[childI], transform);
            }
        }
    }

    private Transform GetLocalTransform(Node node) {
        Transform transform;

        if (node.ShouldSerializeMatrix()) {
            var matrix = new Matrix4x4(
                node.Matrix[0], node.Matrix[1], node.Matrix[2], node.Matrix[3],
                node.Matrix[4], node.Matrix[5], node.Matrix[6], node.Matrix[7],
                node.Matrix[8], node.Matrix[9], node.Matrix[10], node.Matrix[11],
                node.Matrix[12], node.Matrix[13], node.Matrix[14], node.Matrix[15]
            );

            Matrix4x4.Decompose(matrix, out transform.Scale, out transform.Rotation, out transform.Position);
        }
        else {
            transform.Position = new Vector3(node.Translation);
            transform.Rotation = new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3]);
            transform.Scale = new Vector3(node.Scale);
        }

        return transform;
    }

    private IMesh GetMesh(int? indexI, int posI, int texCoordI, int normalI) {
        var key = (indexI, posI, texCoordI, normalI);

        if (!meshes.TryGetValue(key, out var mesh)) {
            mesh = new GltfIMesh(
                gltf,
                gltfPath,
                indexI != null ? gltf.Accessors[indexI.Value] : null,
                gltf.Accessors[posI],
                gltf.Accessors[texCoordI],
                normalI != -1 ? gltf.Accessors[normalI] : null
            );

            meshes[key] = mesh;
        }

        return mesh;
    }

    private Material GetMaterial(int? index) {
        // Default material

        if (index == null) {
            defaultMaterial ??= new Material {
                Albedo = Vector4.One,
                Roughness = 0.5f,
                Metallic = 0
            };

            return defaultMaterial;
        }

        // Convert material

        var gltfMaterial = gltf.Materials[index.Value];

        if (!materials.TryGetValue(gltfMaterial, out var material)) {
            material = new Material();

            if (gltfMaterial.PbrMetallicRoughness != null) {
                var pbr = gltfMaterial.PbrMetallicRoughness;

                if (pbr.BaseColorTexture != null) {
                    material.AlbedoMap = GetRgbaTexture(gltf.Textures[pbr.BaseColorTexture.Index]);
                }

                material.Albedo = new Vector4(pbr.BaseColorFactor);

                if (pbr.MetallicRoughnessTexture != null) {
                    material.RoughnessMap = GetRoughnessTexture(gltf.Textures[pbr.MetallicRoughnessTexture.Index]);
                    material.MetallicMap = GetMetallicTexture(gltf.Textures[pbr.MetallicRoughnessTexture.Index]);
                }

                material.Roughness = pbr.RoughnessFactor;
                material.Metallic = pbr.MetallicFactor;
            }
            else if (gltfMaterial.Extensions.TryGetValue("KHR_materials_pbrSpecularGlossiness", out var extension)) {
                var ext = (JObject) extension;

                var diffuseTexture = ext["diffuseTexture"];
                if (diffuseTexture != null) {
                    material.AlbedoMap = GetRgbaTexture(gltf.Textures[diffuseTexture["index"].Value<uint>()]);
                }

                var diffuse = ext["diffuseFactor"].Values<float>().ToArray();
                material.Albedo = Rgba.From(diffuse);
            }

            materials[gltfMaterial] = material;
        }

        return material;
    }

    private ITexture GetRgbaTexture(GltfTexture gltfTexture) {
        if (!rgbaTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltf.Images[gltfTexture.Source!.Value];
            texture = new GltfRgbaTexture(gltf, gltfPath, image);

            rgbaTextures[gltfTexture] = texture;
        }

        return texture;
    }

    private ITexture GetRoughnessTexture(GltfTexture gltfTexture) {
        if (!roughnessTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltf.Images[gltfTexture.Source!.Value];
            texture = new GltfRoughnessMetallicTexture(gltf, gltfPath, image, 1);

            roughnessTextures[gltfTexture] = texture;
        }

        return texture;
    }

    private ITexture GetMetallicTexture(GltfTexture gltfTexture) {
        if (!metallicTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltf.Images[gltfTexture.Source!.Value];
            texture = new GltfRoughnessMetallicTexture(gltf, gltfPath, image, 2);

            metallicTextures[gltfTexture] = texture;
        }

        return texture;
    }

    internal static Stream GetStream(Gltf gltf, string gltfPath, int viewI) {
        var view = gltf.BufferViews[viewI];
        var buffer = gltf.Buffers[view.Buffer];

        var stream = GetStream(gltfPath, buffer);
        stream.Seek(view.ByteOffset, SeekOrigin.Current);

        return stream;
    }

    internal static Stream GetStream(Gltf gltf, string gltfPath, Accessor accessor) {
        var stream = GetStream(gltf, gltfPath, accessor.BufferView!.Value);
        stream.Seek(accessor.ByteOffset, SeekOrigin.Current);

        return stream;
    }

    internal static Stream GetStream(Gltf gltf, string gltfPath, GltfImage image) {
        // Buffer view

        if (image.BufferView != null)
            return GetStream(gltf, gltfPath, image.BufferView!.Value);

        // URI

        return GetUriStream(gltfPath, image.Uri);
    }

    private static Stream GetStream(string gltfPath, Buffer buffer) {
        // GLB Binary chunk

        if (buffer.Uri == null) {
            var reader = new BinaryReader(new FileStream(gltfPath, FileMode.Open));

            ReadBinaryHeader(reader);
            SeekToBinaryChunk(reader, 5130562);

            return reader.BaseStream;
        }

        // URI

        return GetUriStream(gltfPath, buffer.Uri);
    }

    private static void ReadBinaryHeader(BinaryReader reader) {
        var magic = reader.ReadUInt32();
        if (magic != 1179937895) {
            throw new InvalidDataException($"Unexpected magic number: {magic}");
        }

        var version = reader.ReadUInt32();
        if (version != 2) {
            throw new InvalidDataException($"Unknown version number: {version}");
        }

        var length = reader.ReadUInt32();
        var fileLength = reader.BaseStream.Length;
        if (length != fileLength) {
            throw new InvalidDataException(
                $"The specified length of the file ({length}) is not equal to the actual length of the file ({fileLength}).");
        }
    }

    private static void SeekToBinaryChunk(BinaryReader reader, uint format) {
        while (true) {
            var chunkLength = reader.ReadUInt32();
            if ((chunkLength & 3) != 0) {
                throw new InvalidDataException($"The chunk must be padded to 4 bytes: {chunkLength}");
            }

            var chunkFormat = reader.ReadUInt32();
            if (chunkFormat == format) return;

            reader.BaseStream.Seek(chunkLength, SeekOrigin.Current);
        }
    }

    private static Stream GetUriStream(string gltfPath, string uri) {
        // Embedded URI data

        {
            var stream = GetUriPrefixStream(uri, "data:application/gltf-buffer;base64,");
            if (stream != null) return stream;

            stream = GetUriPrefixStream(uri, "data:application/octet-stream;base64,");
            if (stream != null) return stream;
        }

        // External file

        var path = Path.Combine(Path.GetDirectoryName(gltfPath)!, Uri.UnescapeDataString(uri));
        return new FileStream(path, FileMode.Open);
    }

    private static MemoryStream? GetUriPrefixStream(string uri, string prefix) {
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var bytes = Convert.FromBase64String(uri[prefix.Length..]);
        return new MemoryStream(bytes);
    }

    public static Model Load(string path) {
        var gltf = Interface.LoadModel(path);
        var loader = new GltfLoader(gltf, path);

        foreach (var nodeI in gltf.Scenes[gltf.Scene ?? 0].Nodes) {
            loader.Load(gltf.Nodes[nodeI], new Transform());
        }

        return loader.model;
    }
}

internal class GltfIMesh : IMesh {
    private readonly Gltf gltf;
    private readonly string gltfPath;

    private readonly Accessor? indexAccessor;
    private readonly Accessor posAccessor;
    private readonly Accessor uvAccessor;
    private readonly Accessor? normalAccessor;

    public GltfIMesh(Gltf gltf, string gltfPath, Accessor? indexAccessor, Accessor posAccessor, Accessor uvAccessor,
        Accessor? normalAccessor) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;

        this.indexAccessor = indexAccessor;
        this.posAccessor = posAccessor;
        this.uvAccessor = uvAccessor;
        this.normalAccessor = normalAccessor;
    }

    public uint? IndexCount => (uint?) indexAccessor?.Count;

    public uint VertexCount => (uint) posAccessor.Count;

    public unsafe void WriteIndices(Span<uint> indices) {
        if (indexAccessor == null)
            throw new Exception();

        using var stream = GltfLoader.GetStream(gltf, gltfPath, indexAccessor);

        switch (indexAccessor.ComponentType) {
            case Accessor.ComponentTypeEnum.UNSIGNED_SHORT:
                var buffered = new BufferedStream(stream);

                ushort index = 0;
                var indexSpan = new Span<byte>((byte*) &index, 2);

                for (var i = 0; i < indexAccessor.Count; i++) {
                    buffered.ReadExactly(indexSpan);
                    indices[i] = index;
                }

                break;

            case Accessor.ComponentTypeEnum.UNSIGNED_INT:
                stream.CopyToSize(
                    new UnmanagedMemoryStream(
                        (byte*) Utils.AsPtr(indices),
                        0,
                        indexAccessor.Count * 4,
                        FileAccess.Write
                    ),
                    indexAccessor.Count * 4
                );

                break;

            default:
                throw new Exception("Invalid GLTF index component type: " + indexAccessor.ComponentType);
        }
    }

    public IEnumerable<Vector3> VertexPositions() {
        return new AccessorEnumerable<Vector3>(gltf, gltfPath, posAccessor);
    }

    public IEnumerable<Vector2> VertexUvs() {
        return new AccessorEnumerable<Vector2>(gltf, gltfPath, uvAccessor);
    }

    public IEnumerable<Vector3>? VertexNormals() {
        return normalAccessor != null ? new AccessorEnumerable<Vector3>(gltf, gltfPath, normalAccessor) : null;
    }
}

internal class AccessorEnumerable<T> : IEnumerable<T> where T : unmanaged {
    private readonly Gltf gltf;
    private readonly string gltfPath;
    private readonly Accessor accessor;

    public AccessorEnumerable(Gltf gltf, string gltfPath, Accessor accessor) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;
        this.accessor = accessor;

        if ((int) Utils.SizeOf<T>() != (gltf.BufferViews[accessor.BufferView!.Value].ByteStride ?? (int) Utils.SizeOf<T>()))
            throw new Exception("Invalid GLTF buffer view stride");
    }

    public IEnumerator<T> GetEnumerator() {
        return new StreamEnumerator<T>(GltfLoader.GetStream(gltf, gltfPath, accessor), (uint) accessor.Count);
    }

    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}

internal class StreamEnumerator<T> : IEnumerator<T> where T : unmanaged {
    private readonly BufferedStream stream;
    private readonly long startPosition;

    private readonly uint count;
    private uint i;

    public StreamEnumerator(Stream stream, uint count) {
        this.stream = new BufferedStream(stream);
        this.startPosition = stream.Position;
        this.count = count;
    }

    public T Current { get; private set; }
    object IEnumerator.Current => Current;

    public unsafe bool MoveNext() {
        if (i >= count)
            return false;

        Unsafe.SkipInit(out T value);
        stream.ReadExactly(new Span<byte>((byte*) &value, (int) Utils.SizeOf<T>()));

        Current = value;
        i++;

        return true;
    }

    public void Reset() {
        stream.Seek(startPosition, SeekOrigin.Begin);
        i = 0;
    }

    public void Dispose() {
        stream.Dispose();
    }
}

internal class GltfRgbaTexture : ITexture {
    private readonly Gltf gltf;
    private readonly string gltfPath;

    private readonly GltfImage gltfImage;

    public TextureFormat Format => TextureFormat.Rgba;
    public Vector2D<uint> Size { get; }

    public GltfRgbaTexture(Gltf gltf, string gltfPath, GltfImage gltfImage) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;

        this.gltfImage = gltfImage;

        // Get size

        using var stream = GltfLoader.GetStream(gltf, gltfPath, gltfImage);
        var size = Image.Identify(stream).Size;

        Size = new Vector2D<uint>((uint) size.Width, (uint) size.Height);
    }

    public void Write(Span<byte> pixels) {
        using var stream = GltfLoader.GetStream(gltf, gltfPath, gltfImage);

        var conf = Configuration.Default.Clone();
        conf.PreferContiguousImageBuffers = true;

        using var image = Image.Load<Rgba32>(new DecoderOptions {
            Configuration = conf
        }, stream);

        if (!image.DangerousTryGetSinglePixelMemory(out var memory))
            throw new Exception("Image is not contiguous");

        MemoryMarshal.Cast<Rgba32, byte>(memory.Span).CopyTo(pixels);
    }
}

internal class GltfRoughnessMetallicTexture : ITexture {
    private readonly Gltf gltf;
    private readonly string gltfPath;

    private readonly GltfImage gltfImage;
    private readonly int component;

    public TextureFormat Format => TextureFormat.R;
    public Vector2D<uint> Size { get; }

    public GltfRoughnessMetallicTexture(Gltf gltf, string gltfPath, GltfImage gltfImage, int component) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;

        this.gltfImage = gltfImage;
        this.component = component;

        // Get size

        using var stream = GltfLoader.GetStream(gltf, gltfPath, gltfImage);
        var size = Image.Identify(stream).Size;

        Size = new Vector2D<uint>((uint) size.Width, (uint) size.Height);
    }

    public void Write(Span<byte> pixels) {
        using var stream = GltfLoader.GetStream(gltf, gltfPath, gltfImage);

        var conf = Configuration.Default.Clone();
        conf.PreferContiguousImageBuffers = true;

        using var image = Image.Load<Rgb24>(new DecoderOptions {
            Configuration = conf
        }, stream);

        if (!image.DangerousTryGetSinglePixelMemory(out var memory))
            throw new Exception("Image is not contiguous");

        var i = 0;

        switch (component) {
            case 0:
                foreach (var pixel in memory.Span) {
                    pixels[i++] = pixel.R;
                }

                break;

            case 1:
                foreach (var pixel in memory.Span) {
                    pixels[i++] = pixel.G;
                }

                break;

            case 2:
                foreach (var pixel in memory.Span) {
                    pixels[i++] = pixel.B;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}