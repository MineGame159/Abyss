using Arch.Core;
using Arch.Core.Extensions;

namespace Abyss.Engine.Scene;

internal static class WorldTransforms {
    private static readonly Dictionary<Entity, Transform> transforms = [];

    public static void Update(World world) {
        transforms.Clear();

        foreach (var entity in world.GetRootEntity().Children()) {
            UpdateEntity(new Transform(), entity);
        }
    }

    public static Transform? Get(Entity entity) {
        return transforms.GetValueOrDefault(entity);
    }

    private static void UpdateEntity(Transform parent, Entity entity) {
        var transform = parent;
        transform.Apply(entity.Get<Transform>());

        transforms[entity] = transform;

        foreach (var child in entity.Children()) {
            UpdateEntity(transform, child);
        }
    }
}