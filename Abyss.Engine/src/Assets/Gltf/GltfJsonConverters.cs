using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Abyss.Engine.Assets.Gltf;

public class GltfExtensionsConverter : JsonConverter<Dictionary<string, object>> {
    public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        var extensions = new Dictionary<string, object>();

        while (reader.Read()) {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            var name = reader.GetString();

            reader.Read();

            if (name == GltfLightsPunctualExt.Name)
                extensions[name] = Deserialize<GltfLightsPunctualExt>(ref reader, options);
            else if (name == GltfPbrSpecularGlossinessExt.Name)
                extensions[name] = Deserialize<GltfPbrSpecularGlossinessExt>(ref reader, options);
            else
                reader.Skip();
        }

        return extensions;
    }

    private static T Deserialize<T>(ref Utf8JsonReader reader, JsonSerializerOptions options) {
        return ((JsonConverter<T>) options.GetConverter(typeof(T))).Read(ref reader, typeof(T), options)!;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}

public class Matrix4X4Converter : JsonConverter<Matrix4x4> {
    public override Matrix4x4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new Exception();

        var matrix = new Matrix4x4();

        for (var i = 0; i < 16; i++) {
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException();

            matrix[i / 4, i % 4] = (float) reader.GetDouble();
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException();

        return matrix;
    }

    public override void Write(Utf8JsonWriter writer, Matrix4x4 value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}

public class QuaternionConverter : JsonConverter<Quaternion> {
    public override Quaternion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new Exception();

        var quat = new Quaternion();

        for (var i = 0; i < 4; i++) {
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException();

            quat[i] = (float) reader.GetDouble();
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException();

        return quat;
    }

    public override void Write(Utf8JsonWriter writer, Quaternion value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}

public class Vector4Converter : JsonConverter<Vector4> {
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new Exception();

        var vec = new Vector4();

        for (var i = 0; i < 4; i++) {
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException();

            vec[i] = (float) reader.GetDouble();
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException();

        return vec;
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}

public class Vector3Converter : JsonConverter<Vector3> {
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new Exception();

        var vec = new Vector3();

        for (var i = 0; i < 3; i++) {
            reader.Read();

            if (reader.TokenType != JsonTokenType.Number)
                throw new JsonException();

            vec[i] = (float) reader.GetDouble();
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.EndArray)
            throw new JsonException();

        return vec;
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options) {
        throw new NotImplementedException();
    }
}