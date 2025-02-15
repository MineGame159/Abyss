using Abyss.Engine.Gui;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace Abyss.Engine;

public static class Input {
    private static readonly bool[] prevKeys = new bool[512];
    private static readonly bool[] keys = new bool[512];

    public static IInputContext Ctx { get; private set; } = null!;

    public static void Init(IWindow window) {
        Ctx = window.CreateInput();

        Ctx.Keyboards[0].KeyDown += (_, key, _) => keys[(int) key] = true;
        Ctx.Keyboards[0].KeyUp += (_, key, _) => keys[(int) key] = false;
    }

    public static void Update() {
        Array.Copy(keys, prevKeys, 512);
    }

    public static bool IsKeyDown(Key key) => !ImGuiImpl.CapturesKeys && keys[(int) key];
    public static bool IsKeyPressed(Key key) => !ImGuiImpl.CapturesKeys && !prevKeys[(int) key] && keys[(int) key];
    public static bool IsKeyReleased(Key key) => !ImGuiImpl.CapturesKeys && prevKeys[(int) key] && !keys[(int) key];
}