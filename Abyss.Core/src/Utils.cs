using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Abyss.Core;

public static class Utils {
    public static unsafe T* AsPtr<T>(ReadOnlySpan<T> span) where T : unmanaged {
        return (T*) Unsafe.AsPointer(ref Unsafe.AsRef(in span.GetPinnableReference()));
    }

    public static unsafe T* AsPtr<T>(Span<T> span) where T : unmanaged {
        return (T*) Unsafe.AsPointer(ref span.GetPinnableReference());
    }

    public static unsafe T* AsPtr<T>(T[] array) where T : unmanaged {
        return (T*) Unsafe.AsPointer(ref array.AsSpan().GetPinnableReference());
    }

    public static unsafe T* AsPtr<T>(List<T> list) where T : unmanaged {
        return (T*) Unsafe.AsPointer(ref CollectionsMarshal.AsSpan(list).GetPinnableReference());
    }

    public static ulong SizeOf<T>() {
        return (ulong) Unsafe.SizeOf<T>();
    }

    public static T Align<T>(T offset, T alignment) where T : IBinaryInteger<T> {
        return (offset + (alignment - T.One)) & ~(alignment - T.One);
    }

    public static float DegToRad(float deg) {
        return deg * (MathF.PI / 180);
    }

    public static float RadToDeg(float rad) {
        return rad * (180 / MathF.PI);
    }

    public static string FormatBytes(ulong bytes) {
        if (bytes / 1024.0 < 1) return $"{bytes} b";
        if (bytes / 1024.0 / 1024.0 < 1) return $"{bytes / 1024.0:F1} kB";
        if (bytes / 1024.0 / 1024.0 / 1024.0 < 1) return $"{bytes / 1024.0 / 1024.0:F1} mB";

        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} gB";
    }

    public static string FormatDuration(TimeSpan duration) {
        if (duration.TotalMilliseconds < 1) return $"{duration.TotalNanoseconds:F1} ns";
        if (duration.TotalSeconds < 1) return $"{duration.TotalMilliseconds:F1} ms";

        return $"{duration.TotalSeconds:F1} s";
    }
}