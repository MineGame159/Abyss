using Abyss.Engine.Scene;
using Arch.Core;
using Arch.Core.Extensions;
using CommunityToolkit.HighPerformance;

namespace Abyss.Engine;

public static class WorldExt {
    private static readonly QueryDescription rootDesc = new QueryDescription().WithAll<Root, Info>();

    public static Entity? GetFirstEntity(this World world, in QueryDescription desc) {
        var chunkEnum = world.Query(desc).GetEnumerator();

        if (!chunkEnum.MoveNext())
            return null;

        if (chunkEnum.Current.Size == 0)
            return null;

        return chunkEnum.Current.Entity(0);
    }

    public static Entity GetRootEntity(this World world) {
        return world.GetFirstEntity(rootDesc)!.Value;
    }

    // Spawn

    public static Entity Spawn<T0>(
        this World world, T0 c0, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0
        );

        parent.Value.AddChild(entity);

        return entity;
    }

    public static Entity Spawn<T0, T1>(
        this World world, T0 c0, T1 c1, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0,
            c1
        );

        parent.Value.AddChild(entity);

        return entity;
    }

    public static Entity Spawn<T0, T1, T2>(
        this World world, T0 c0, T1 c1, T2 c2, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0,
            c1,
            c2
        );

        parent.Value.AddChild(entity);

        return entity;
    }

    public static Entity Spawn<T0, T1, T2, T3>(
        this World world, T0 c0, T1 c1, T2 c2, T3 c3, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0,
            c1,
            c2,
            c3
        );

        parent.Value.AddChild(entity);

        return entity;
    }

    public static Entity Spawn<T0, T1, T2, T3, T4>(
        this World world, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0,
            c1,
            c2,
            c3,
            c4
        );

        parent.Value.AddChild(entity);

        return entity;
    }

    public static Entity Spawn<T0, T1, T2, T3, T4, T5>(
        this World world, T0 c0, T1 c1, T2 c2, T3 c3, T4 c4, T5 c5, Entity? parent = null, string name = ""
    ) {
        parent ??= world.GetRootEntity();

        var entity = world.Create(
            new Info {
                Name = name,
                Visible = true,
                Parent = parent
            },
            c0,
            c1,
            c2,
            c3,
            c4,
            c5
        );

        parent.Value.AddChild(entity);

        return entity;
    }
}

public static class EntityExt {
    public static void AddChild(this Entity entity, Entity child) {
        ref var info = ref entity.Get<Info>();

        info.Children ??= [];
        info.Children.Add(child);
    }

    public static IEnumerable<Entity> Children(this Entity entity) {
        ref var info = ref entity.Get<Info>();
        return info.Children ?? Enumerable.Empty<Entity>();
    }

    public static bool IsLeaf(this Entity entity) {
        ref var info = ref entity.Get<Info>();
        return info.Children == null || info.Children.Count == 0;
    }

    // Delete

    public static void Delete(this Entity entity) {
        if (!entity.IsLeaf())
            throw new Exception("Can only delete entities with no children");

        ref var info = ref entity.Get<Info>();

        ref var parentInfo = ref info.Parent!.Value.Get<Info>();
        parentInfo.Children!.Remove(entity);

        World.Worlds.DangerousGetReferenceAt(entity.WorldId).Destroy(entity);
    }

    public static void DeleteHierarchy(this Entity entity) {
        foreach (var child in entity.Children()) {
            child.DeleteHierarchy();
        }

        entity.Delete();
    }
}