using Abyss.Engine.Assets.Gltf;
using Abyss.Engine.Scene;
using Arch.Core;

namespace Abyss.Engine.Assets;

public class Model {
    internal readonly List<EntityInfo> Infos = [];

    internal Model() { }

    public static Model Load(string path) {
        return Path.GetExtension(path) switch {
            ".gltf" or ".glb" => GltfLoader.Load(path),
            _ => throw new Exception("Invalid model extension: " + Path.GetExtension(path))
        };
    }

    public void Spawn(World world, Transform transform) {
        var root = world.GetRootEntity();

        foreach (var info in Infos) {
            SpawnEntity(world, root, info, transform);
        }
    }

    private static void SpawnEntity(World world, Entity parent, EntityInfo info, Transform transform) {
        var entityTransform = transform;
        entityTransform.Apply(info.Transform);

        var entity = default(Entity);

        if (info.Instance != null)
            entity = world.Spawn(entityTransform, info.Instance!.Value, parent: parent, name: info.Name);
        else if (info.PointLight != null)
            entity = world.Spawn(entityTransform, info.PointLight!.Value, parent: parent, name: info.Name);
        else if (info.DirectionalLight != null)
            entity = world.Spawn(entityTransform, info.DirectionalLight!.Value, parent: parent, name: info.Name);
        else
            entity = world.Spawn(entityTransform, parent: parent, name: info.Name);

        foreach (var childInfo in info.Children) {
            SpawnEntity(world, entity, childInfo, new Transform());
        }
    }

    internal class EntityInfo {
        public string Name;

        public Transform Transform;
        public MeshInstance? Instance;
        public PointLight? PointLight;
        public DirectionalLight? DirectionalLight;

        public List<EntityInfo> Children;
    }
}