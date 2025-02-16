using Abyss.Engine.Scene;
using Arch.Core;

namespace Abyss.Engine.Assets;

public class Model {
    public readonly List<(string, Transform, MeshInstance?, PointLight?, DirectionalLight?)> Entities = [];

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

            var info = new Info(entity.Item1);

            if (entity.Item3 != null)
                world.Create(info, entityTransform, entity.Item3!.Value);
            else if (entity.Item4 != null)
                world.Create(info, entityTransform, entity.Item4!.Value);
            else if (entity.Item5 != null)
                world.Create(info, entityTransform, entity.Item5!.Value);
        }
    }
}