using Arch.Core;

namespace Abyss.Engine;

public static class WorldExt {
    public static Entity? GetFirstEntity(this World world, in QueryDescription desc) {
        var chunkEnum = world.Query(desc).GetEnumerator();

        if (!chunkEnum.MoveNext())
            return null;

        if (chunkEnum.Current.Size == 0)
            return null;

        return chunkEnum.Current.Entity(0);
    }
}