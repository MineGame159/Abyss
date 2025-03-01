using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Engine.Scene;
using Silk.NET.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;

namespace Abyss.Engine.Assets.Gltf;

internal class GltfLoader {
    private readonly GltfFile gltf;
    private readonly string gltfPath;

    private readonly Dictionary<(int?, int, int, int?), IMesh> meshes = [];

    private Material? defaultMaterial;
    private readonly Dictionary<GltfMaterial, Material> materials = [];

    private readonly Dictionary<GltfTexture, ITexture> rgbaTextures = [];
    private readonly Dictionary<GltfTexture, ITexture> roughnessTextures = [];
    private readonly Dictionary<GltfTexture, ITexture> metallicTextures = [];

    public GltfLoader(GltfFile gltf, string gltfPath) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;
    }

    private Model.EntityInfo Load(GltfNode node) {
        var info = new Model.EntityInfo {
            Name = node.Name ?? "",
            Transform = GetLocalTransform(node),
            Children = []
        };

        if (node.Mesh != null) {
            foreach (var primitive in node.Mesh.Primitives) {
                if (primitive.Mode != GltfPrimitiveMode.Triangles)
                    continue;

                var posI = primitive.Attributes.GetValueOrDefault("POSITION", -1);
                if (posI == -1) continue;

                var texCoordI = primitive.Attributes.GetValueOrDefault("TEXCOORD_0", -1);
                if (texCoordI == -1) continue;

                var normalI = primitive.Attributes.GetValueOrDefault("NORMAL", -1);

                var mesh = GetMesh(primitive.Indices, posI, texCoordI, normalI);
                var material = GetMaterial(primitive.Material);

                info.Instance = new MeshInstance(mesh, material);
            }
        }
        else if (node.TryGetExtension(out GltfLightsPunctualExt ext)) {
            var light = ext.Light!;

            switch (light.Type) {
                case GltfLightType.Point: {
                    var color = light.Color;
                    var intensity = light.Intensity / 1000;

                    info.PointLight = new PointLight(color, intensity);
                    break;
                }
                case GltfLightType.Directional: {
                    var color = light.Color;
                    var intensity = light.Intensity / 1000;
                    var direction = Vector3.Transform(new Vector3(0, 0, -1), info.Transform.Matrix);

                    info.DirectionalLight = new DirectionalLight(direction, color, intensity);
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        foreach (var child in node) {
            info.Children.Add(Load(child));
        }

        return info;
    }

    private Transform GetLocalTransform(GltfNode node) {
        Transform transform;

        if (!node.Matrix.IsIdentity) {
            Matrix4x4.Decompose(node.Matrix, out transform.Scale, out transform.Rotation, out transform.Position);
        }
        else {
            transform.Position = node.Translation;
            transform.Rotation = node.Rotation;
            transform.Scale = node.Scale;
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
                Roughness = 1,
                Metallic = 1,
                AlphaCutoff = 0,
                Opaque = true
            };

            return defaultMaterial;
        }

        // Convert material

        var gltfMaterial = gltf.Materials[index.Value];

        if (!materials.TryGetValue(gltfMaterial, out var material)) {
            material = new Material();

            if (gltfMaterial.PbrMetallicRoughness != null) {
                var pbr = gltfMaterial.PbrMetallicRoughness;

                material.AlbedoMap = GetRgbaTexture(pbr.BaseColorTexture?.Texture);
                material.Albedo = pbr.BaseColorFactor;

                material.RoughnessMap = GetRoughnessTexture(pbr.MetallicRoughnessTexture?.Texture);
                material.Roughness = pbr.RoughnessFactor;

                material.MetallicMap = GetMetallicTexture(pbr.MetallicRoughnessTexture?.Texture);
                material.Metallic = pbr.MetallicFactor;
            }
            else if (gltfMaterial.TryGetExtension(out GltfPbrSpecularGlossinessExt ext)) {
                material.AlbedoMap = GetRgbaTexture(ext.DiffuseTexture?.Texture);
                material.Albedo = ext.DiffuseFactor;
            }

            material.EmissiveMap = GetRgbaTexture(gltfMaterial.EmissiveTexture?.Texture);
            material.Emissive = gltfMaterial.EmissiveFactor;

            material.AlphaCutoff = 0;
            material.Opaque = true;

            material.NormalMap = GetRgbaTexture(gltfMaterial.NormalTexture?.Texture);

            switch (gltfMaterial.AlphaMode) {
                case GltfAlphaMode.Mask:
                    material.AlphaCutoff = gltfMaterial.AlphaCutoff + float.Epsilon;
                    break;

                case GltfAlphaMode.Blend:
                    material.Opaque = false;
                    break;
            }

            materials[gltfMaterial] = material;
        }

        return material;
    }

    private ITexture? GetRgbaTexture(GltfTexture? gltfTexture) {
        if (gltfTexture == null)
            return null;

        if (!rgbaTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltfTexture.Image!;
            texture = new GltfRgbaTexture(gltf, gltfPath, image);

            rgbaTextures[gltfTexture] = texture;
        }

        return texture;
    }

    private ITexture? GetRoughnessTexture(GltfTexture? gltfTexture) {
        if (gltfTexture == null)
            return null;

        if (!roughnessTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltfTexture.Image!;
            texture = new GltfRoughnessMetallicTexture(gltf, gltfPath, image, 1);

            roughnessTextures[gltfTexture] = texture;
        }

        return texture;
    }

    private ITexture? GetMetallicTexture(GltfTexture? gltfTexture) {
        if (gltfTexture == null)
            return null;

        if (!metallicTextures.TryGetValue(gltfTexture, out var texture)) {
            var image = gltfTexture.Image!;
            texture = new GltfRoughnessMetallicTexture(gltf, gltfPath, image, 2);

            metallicTextures[gltfTexture] = texture;
        }

        return texture;
    }

    internal static Stream GetStream(GltfFile gltf, string gltfPath, GltfBufferView view) {
        var buffer = view.Buffer;

        var stream = GetStream(gltfPath, buffer);
        stream.Seek(view.ByteOffset, SeekOrigin.Current);

        return stream;
    }

    internal static Stream GetStream(GltfFile gltf, string gltfPath, GltfAccessor accessor) {
        var stream = GetStream(gltf, gltfPath, accessor.BufferView!);
        stream.Seek(accessor.ByteOffset, SeekOrigin.Current);

        return stream;
    }

    internal static Stream GetStream(GltfFile gltf, string gltfPath, GltfImage image) {
        // Buffer view

        if (image.BufferView != null)
            return GetStream(gltf, gltfPath, image.BufferView!);

        // URI

        return GetUriStream(gltfPath, image.Uri!);
    }

    private static Stream GetStream(string gltfPath, GltfBuffer buffer) {
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
        var gltf = GltfFile.Load(path)!;
        var loader = new GltfLoader(gltf, path);

        var model = new Model();

        foreach (var node in gltf.Scenes[gltf.Scene ?? 0]) {
            model.Infos.Add(loader.Load(node));
        }

        return model;
    }
}

internal class GltfIMesh : IMesh {
    private readonly GltfFile gltf;
    private readonly string gltfPath;

    private readonly GltfAccessor? indexAccessor;
    private readonly GltfAccessor posAccessor;
    private readonly GltfAccessor uvAccessor;
    private readonly GltfAccessor? normalAccessor;

    public GltfIMesh(GltfFile gltf, string gltfPath, GltfAccessor? indexAccessor, GltfAccessor posAccessor, GltfAccessor uvAccessor,
        GltfAccessor? normalAccessor) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;

        this.indexAccessor = indexAccessor;
        this.posAccessor = posAccessor;
        this.uvAccessor = uvAccessor;
        this.normalAccessor = normalAccessor;
    }

    public uint? IndexCount => indexAccessor?.Count;

    public uint VertexCount => posAccessor.Count;

    public unsafe void WriteIndices(Span<uint> indices) {
        if (indexAccessor == null)
            throw new Exception();

        using var stream = GltfLoader.GetStream(gltf, gltfPath, indexAccessor);

        switch (indexAccessor.ComponentType) {
            case GltfComponentType.UnsignedShort:
                var buffered = new BufferedStream(stream);

                ushort index = 0;
                var indexSpan = new Span<byte>((byte*) &index, 2);

                for (var i = 0; i < indexAccessor.Count; i++) {
                    buffered.ReadExactly(indexSpan);
                    indices[i] = index;
                }

                break;

            case GltfComponentType.UnsignedInt:
                stream.CopyToSize(
                    new UnmanagedMemoryStream(
                        (byte*) Utils.AsPtr(indices),
                        0,
                        indexAccessor.Count * 4,
                        FileAccess.Write
                    ),
                    (int) (indexAccessor.Count * 4)
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
    private readonly GltfFile gltf;
    private readonly string gltfPath;
    private readonly GltfAccessor accessor;

    public AccessorEnumerable(GltfFile gltf, string gltfPath, GltfAccessor accessor) {
        this.gltf = gltf;
        this.gltfPath = gltfPath;
        this.accessor = accessor;

        if (Utils.SizeOf<T>() != (accessor.BufferView?.ByteStride ?? Utils.SizeOf<T>()))
            throw new Exception("Invalid GLTF buffer view stride");
    }

    public IEnumerator<T> GetEnumerator() {
        return new StreamEnumerator<T>(GltfLoader.GetStream(gltf, gltfPath, accessor), accessor.Count);
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
    private readonly GltfFile gltf;
    private readonly string gltfPath;

    private readonly GltfImage gltfImage;

    public TextureFormat Format => TextureFormat.Rgba;
    public Vector2D<uint> Size { get; }

    public GltfRgbaTexture(GltfFile gltf, string gltfPath, GltfImage gltfImage) {
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
    private readonly GltfFile gltf;
    private readonly string gltfPath;

    private readonly GltfImage gltfImage;
    private readonly int component;

    public TextureFormat Format => TextureFormat.R;
    public Vector2D<uint> Size { get; }

    public GltfRoughnessMetallicTexture(GltfFile gltf, string gltfPath, GltfImage gltfImage, int component) {
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