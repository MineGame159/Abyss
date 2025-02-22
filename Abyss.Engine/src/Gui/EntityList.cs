using Abyss.Engine.Scene;
using Arch.Core;
using Arch.Core.Extensions;
using Hexa.NET.ImGui;

namespace Abyss.Engine.Gui;

internal static class EntityList {
    public static EntityReference SelectedEntity;

    public static void Render(World world) {
        if (!ImGui.Begin("Entities")) {
            ImGui.End();
            return;
        }

        foreach (var entity in world.GetRootEntity().Children()) {
            RenderEntity(entity);
        }

        ImGui.End();
    }

    private static void RenderEntity(Entity entity) {
        ImGui.PushID(entity.Id);

        var name = GetEntityName(entity, out var visible);

        if (!visible) {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        }

        var selected = SelectedEntity == entity;

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
        if (selected) flags |= ImGuiTreeNodeFlags.Selected;
        if (entity.IsLeaf()) flags |= ImGuiTreeNodeFlags.Bullet | ImGuiTreeNodeFlags.Leaf;

        var expanded = ImGui.TreeNodeEx(name, flags);
        if ((ImGui.IsItemClicked() || ImGui.IsItemClicked(ImGuiMouseButton.Right)) && !ImGui.IsItemToggledOpen()) selected = !selected;

        if (selected) SelectedEntity = entity.Reference();
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right) && !ImGui.IsItemToggledOpen()) ToggleVisibility();

        if (!visible) {
            ImGui.PopStyleColor();
        }

        if (expanded) {
            foreach (var child in entity.Children()) {
                RenderEntity(child);
            }

            ImGui.TreePop();
        }

        ImGui.PopID();
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