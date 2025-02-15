using Abyss.Engine.Scene;
using Arch.Core;

namespace Abyss.Engine.Assets;

public class Model {
    public readonly List<(string, Transform, MeshInstance)> Entities = [];

    internal Model() { }

    public static Model Load(string path) {
        return Path.GetExtension(path) switch {
            ".gltf" or ".glb" => GltfLoader.Load(path),
            _ => throw new Exception("Invalid model extension: " + Path.GetExtension(path))
        };
    }

    public void Spawn(World world, Transform transform) {
        foreach (var entity in Entities) {
            var entityTransform = transform;
            entityTransform.Apply(entity.Item2);

            world.Create(new Info(entity.Item1), entityTransform, entity.Item3);
        }
    }
}