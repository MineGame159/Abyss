using System.Numerics;
using Abyss.Engine.Render;
using Abyss.Engine.Scene;
using Arch.Core;
using Arch.Core.Extensions;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.Input;

namespace Abyss.Engine.Gui;

internal static class AbyssGui {
    private static World world = null!;
    private static Renderer renderer = null!;

    private static bool visible;

    public static void Init(Renderer renderer, World world) {
        AbyssGui.world = world;
        AbyssGui.renderer = renderer;

        var style = ImGui.GetStyle();

        style.WindowRounding = 3;
        style.FrameRounding = 3;

        style.IndentSpacing = 12;
    }

    public static void Render() {
        if (Input.IsKeyReleased(Key.G))
            visible = !visible;

        if (!visible)
            return;

        EntityList.Render(world);
        Inspector.Render(EntityList.SelectedEntity);

        if (EntityList.SelectedEntity.IsAlive())
            RenderGuizmo(EntityList.SelectedEntity);
    }

    private static void RenderGuizmo(Entity entity) {
        var view = renderer.ViewMatrix;
        var projection = renderer.ProjectionMatrix;

        var matrix = GetWorldTransform(entity).Matrix;
        var delta = Matrix4x4.Identity;

        ImGuizmo.Enable(true);
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(0, 0, renderer.Ctx.Swapchain.FramebufferSize.X, renderer.Ctx.Swapchain.FramebufferSize.Y);

        if (ImGuizmo.Manipulate(ref view, ref projection, ImGuizmoOperation.Universal, ImGuizmoMode.Local, ref matrix, ref delta)) {
            var deltaTransform = new Transform(delta);

            (deltaTransform.Position.Y, deltaTransform.Position.Z) = (deltaTransform.Position.Z, deltaTransform.Position.Y);
            deltaTransform.Position.Y *= -1;
            (deltaTransform.Rotation.Y, deltaTransform.Rotation.Z) = (deltaTransform.Rotation.Z, deltaTransform.Rotation.Y);
            deltaTransform.Rotation.Y *= -1;

            ref var transform = ref entity.Get<Transform>();
            transform.Apply(deltaTransform);
        }
    }

    private static Transform GetWorldTransform(Entity entity) {
        var transform = entity.Get<Transform>();
        var parent = entity.Parent();

        if (parent != null) {
            var a = GetWorldTransform(parent.Value);
            a.Apply(transform);

            return a;
        }

        return transform;
    }
}