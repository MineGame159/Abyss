using Arch.Core;
using Hexa.NET.ImGui;
using Silk.NET.Input;

namespace Abyss.Engine.Gui;

internal static class AbyssGui {
    private static World world = null!;

    private static bool visible;
    private static EntityReference selectedEntity = EntityReference.Null;

    public static void Init(World world) {
        AbyssGui.world = world;

        var style = ImGui.GetStyle();

        style.WindowRounding = 3;
        style.FrameRounding = 3;
    }

    public static void Render() {
        if (Input.IsKeyReleased(Key.G))
            visible = !visible;

        if (!visible)
            return;

        if (!selectedEntity.IsAlive())
            selectedEntity = EntityReference.Null;

        EntityList.Render(world);
        Inspector.Render(EntityList.SelectedEntity);
    }
}