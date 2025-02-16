using Abyss.Engine.Scene;
using Arch.Core;
using Arch.Core.Extensions;
using Hexa.NET.ImGui;

namespace Abyss.Engine.Gui;

internal static class EntityList {
    private static readonly QueryDescription allDesc = new();

    public static EntityReference SelectedEntity;

    public static void Render(World world) {
        if (!ImGui.Begin("Entities")) {
            ImGui.End();
            return;
        }

        world.Query(allDesc, entity => {
            ImGui.PushID(entity.Id);

            var name = GetEntityName(entity, out var visible);

            if (!visible) {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            }

            var selected = SelectedEntity == entity;
            var clicked = ImGui.Selectable(name, ref selected, ImGuiSelectableFlags.AllowDoubleClick);

            if (selected) SelectedEntity = entity.Reference();
            if (clicked && ImGuiP.IsMouseDoubleClicked(ImGuiMouseButton.Left)) ToggleVisibility();

            if (!visible) {
                ImGui.PopStyleColor();
            }

            ImGui.PopID();
        });

        ImGui.End();
    }

    private static void ToggleVisibility() {
        ref var info = ref SelectedEntity.Entity.TryGetRef<Info>(out var exists);
        if (exists) info.Visible = !info.Visible;
    }

    private static string GetEntityName(Entity entity, out bool visible) {
        if (entity.TryGet<Info>(out var info)) {
            visible = info.Visible;

            if (info.Name != "")
                return info.Name;
        }
        else {
            visible = true;
        }

        return "Entity " + entity.Id;
    }
}