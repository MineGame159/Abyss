using System.Buffers;
using System.Runtime.InteropServices;

namespace Abyss.Core;

public static class EnumerableExt {
    public static T? FirstNullable<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : struct {
        return source
            .Where(predicate)
            .Select(T? (item) => item)
            .FirstOrDefault();
    }
}

public static class ReadOnlySpanExt {
    public static bool Contains<T>(this ReadOnlySpan<T?> span, T key, EqualityComparer<T?> comparer) {
        foreach (var item in span)
            if (comparer.Equals(item, key))
                return true;

        return false;
    }

    public static int NonNullCount<T>(this ReadOnlySpan<T?> span) {
        var count = 0;

        foreach (var item in span)
            if (item != null)
                count++;

        return count;
    }
}

public static class ListExt {
    public static ref T Ref<T>(this List<T> list, int i) {
        return ref CollectionsMarshal.AsSpan(list)[i];
    }
}

public static class StreamExt {
    public static void CopyToSize(this Stream src, Stream dst, int bytes) {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try {
            int read;

            while (bytes > 0 && (read = src.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0) {
                dst.Write(buffer, 0, read);
                bytes -= read;
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}