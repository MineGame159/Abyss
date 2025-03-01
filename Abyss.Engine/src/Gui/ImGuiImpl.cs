using System.Numerics;
using System.Runtime.InteropServices;
using Abyss.Core;
using Abyss.Gpu;
using Abyss.Gpu.Pipeline;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Silk.NET.Core.Native;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using DescriptorType = Abyss.Gpu.DescriptorType;

namespace Abyss.Engine.Gui;

[StructLayout(LayoutKind.Sequential)]
file record struct DrawData {
    public Vector2 Scale;
    public Vector2 Translate;
};

public static class ImGuiImpl {
    private static GpuContext ctx = null!;

    private static GpuGraphicsPipeline? pipeline;
    private static GpuImage fontImage = null!;
    private static Sampler sampler;

    public static void Init(GpuContext ctx) {
        ImGuiImpl.ctx = ctx;

        ImGui.CreateContext();

        var io = ImGui.GetIO();

        // Platform

        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        unsafe {
            io.BackendPlatformName = (byte*) SilkMarshal.StringToPtr("imgui_impl_abyss");
        }

        Input.Ctx.Mice[0].MouseMove += (_, pos) => {
            pos *= ImGuiImpl.ctx.Swapchain.FramebufferScale;
            ImGui.GetIO().AddMousePosEvent(pos.X, pos.Y);
        };
        Input.Ctx.Mice[0].MouseDown += (_, button) => ImGui.GetIO().AddMouseButtonEvent((int) button, true);
        Input.Ctx.Mice[0].MouseUp += (_, button) => ImGui.GetIO().AddMouseButtonEvent((int) button, false);
        Input.Ctx.Mice[0].Scroll += (_, wheel) => ImGui.GetIO().AddMouseWheelEvent(wheel.X, wheel.Y);

        Input.Ctx.Keyboards[0].KeyDown += (_, key, scancode) => {
            var imKey = ConvertKey(key);

            if (imKey != null) {
                ImGui.GetIO().AddKeyEvent(imKey.Value, true);
                ImGui.GetIO().SetKeyEventNativeData(imKey.Value, (int) key, scancode);
            }
        };
        Input.Ctx.Keyboards[0].KeyUp += (_, key, scancode) => {
            var imKey = ConvertKey(key);

            if (imKey != null) {
                ImGui.GetIO().AddKeyEvent(imKey.Value, false);
                ImGui.GetIO().SetKeyEventNativeData(imKey.Value, (int) key, scancode);
            }
        };
        Input.Ctx.Keyboards[0].KeyChar += (_, c) => {
            ImGui.GetIO().AddInputCharactersUTF8(MemoryMarshal.Cast<char, byte>(new ReadOnlySpan<char>(ref c)));
        };

        // Renderer

        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;

        unsafe {
            io.BackendRendererName = (byte*) SilkMarshal.StringToPtr("imgui_impl_abyss");
        }

        CreateGpuResources(ctx);

        // Other

        ImGuizmo.SetImGuiContext(ImGui.GetCurrentContext());
    }

    public static bool CapturesKeys => ImGui.GetIO().WantCaptureKeyboard;

    public static void BeginFrame(float delta) {
        var io = ImGui.GetIO();

        io.DisplaySize = ctx.Swapchain.FramebufferSize.As<float>().ToSystem();
        io.DisplayFramebufferScale = new Vector2(1, 1);
        io.DeltaTime = delta;

        UpdateMouseCursor();
        UpdateKeyModifiers();

        ImGui.NewFrame();
        ImGuizmo.BeginFrame();
    }

    private static void UpdateMouseCursor() {
        var io = ImGui.GetIO();
        var cursor = Input.Ctx.Mice[0].Cursor;

        if (io.ConfigFlags.HasFlag(ImGuiConfigFlags.NoMouseCursorChange) || cursor.CursorMode == CursorMode.Hidden)
            return;

        var imGuiCursor = ImGui.GetMouseCursor();

        if (imGuiCursor == ImGuiMouseCursor.None || io.MouseDrawCursor) {
            cursor.CursorMode = CursorMode.Hidden;
        }
        else {
            cursor.CursorMode = CursorMode.Normal;

            cursor.StandardCursor = imGuiCursor switch {
                ImGuiMouseCursor.Arrow => StandardCursor.Arrow,
                ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
                ImGuiMouseCursor.ResizeNs => StandardCursor.VResize,
                ImGuiMouseCursor.ResizeEw => StandardCursor.HResize,
                ImGuiMouseCursor.ResizeNesw => StandardCursor.NeswResize,
                ImGuiMouseCursor.ResizeNwse => StandardCursor.NwseResize,
                ImGuiMouseCursor.Hand => StandardCursor.Hand,
                ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    private static void UpdateKeyModifiers() {
        var io = ImGui.GetIO();

        io.AddKeyEvent(ImGuiKey.ModCtrl, Input.IsKeyDown(Key.ControlLeft) || Input.IsKeyDown(Key.ControlRight));
        io.AddKeyEvent(ImGuiKey.ModShift, Input.IsKeyDown(Key.ShiftLeft) || Input.IsKeyDown(Key.ShiftRight));
        io.AddKeyEvent(ImGuiKey.ModAlt, Input.IsKeyDown(Key.AltLeft) || Input.IsKeyDown(Key.AltRight));
        io.AddKeyEvent(ImGuiKey.ModSuper, Input.IsKeyDown(Key.SuperLeft) || Input.IsKeyDown(Key.SuperRight));
    }

    public static void EndFrame(GpuCommandBuffer commandBuffer, GpuImage image) {
        ImGui.EndFrame();
        ImGui.Render();

        var data = ImGui.GetDrawData();

        var fbWidth = (int) (data.DisplaySize.X * data.FramebufferScale.X);
        var fbHeight = (int) (data.DisplaySize.Y * data.FramebufferScale.Y);
        if (fbWidth <= 0 || fbHeight <= 0) return;

        if (data.CmdListsCount == 0) return;

        if (pipeline == null)
            CreatePipeline(commandBuffer.Ctx, image.Format);

        commandBuffer.BeginRenderPass(new Attachment(image, AttachmentLoadOp.Load, AttachmentStoreOp.Store, null));

        commandBuffer.BindPipeline(pipeline!);
        UploadBuffers(commandBuffer, data);
        commandBuffer.BindDescriptorSet(0, new GpuImageSampler(fontImage, sampler));

        commandBuffer.PushConstants(new DrawData {
            Scale = new Vector2(2) / data.DisplaySize,
            Translate = -Vector2.One - data.DisplayPos * (new Vector2(2) / data.DisplaySize)
        });

        var clipOff = data.DisplayPos;
        var clipScale = data.FramebufferScale;

        var indexOffset = 0u;
        var vertexOffset = 0u;

        for (var i = 0; i < data.CmdListsCount; i++) {
            var cmdList = data.CmdLists[i];

            for (var j = 0; j < cmdList.CmdBuffer.Size; j++) {
                var cmd = cmdList.CmdBuffer[j];

                unsafe {
                    if (cmd.UserCallback != null) {
                        throw new Exception("ImGui User Callback not supported");
                    }
                }

                // Project scissor/clipping rectangles into framebuffer space
                var clipMin = new Vector2((cmd.ClipRect.X - clipOff.X) * clipScale.X, (cmd.ClipRect.Y - clipOff.Y) * clipScale.Y);
                var clipMax = new Vector2((cmd.ClipRect.Z - clipOff.X) * clipScale.X, (cmd.ClipRect.W - clipOff.Y) * clipScale.Y);

                // Clamp to viewport as vkCmdSetScissor() won't accept values that are off bounds
                if (clipMin.X < 0.0f) clipMin.X = 0.0f;
                if (clipMin.Y < 0.0f) clipMin.Y = 0.0f;
                if (clipMax.X > fbWidth) clipMax.X = fbWidth;
                if (clipMax.Y > fbHeight) clipMax.Y = fbHeight;
                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y) continue;

                commandBuffer.SetScissor(clipMin.ToGeneric().As<int>(), clipMax.ToGeneric().As<int>());
                commandBuffer.DrawIndexed(cmd.ElemCount, 1, cmd.IdxOffset + indexOffset, (int) (cmd.VtxOffset + vertexOffset));
            }

            indexOffset += (uint) cmdList.IdxBuffer.Size;
            vertexOffset += (uint) cmdList.VtxBuffer.Size;
        }

        commandBuffer.EndRenderPass();
    }

    private static void UploadBuffers(GpuCommandBuffer commandBuffer, ImDrawDataPtr data) {
        var vertexSize = (ulong) data.TotalVtxCount * Utils.SizeOf<ImDrawVert>();
        var vertexBuffer = commandBuffer.Ctx.FrameAllocator.Allocate(BufferUsageFlags.VertexBufferBit, vertexSize);
        var vertices = vertexBuffer.Map<ImDrawVert>();

        var indexSize = (ulong) data.TotalIdxCount * Utils.SizeOf<ushort>();
        var indexBuffer = commandBuffer.Ctx.FrameAllocator.Allocate(BufferUsageFlags.IndexBufferBit, indexSize);
        var indices = indexBuffer.Map<ushort>();

        for (var i = 0; i < data.CmdListsCount; i++) {
            var cmdList = data.CmdLists[i];

            unsafe {
                var b = new ReadOnlySpan<ImDrawVert>(cmdList.VtxBuffer.Data, cmdList.VtxBuffer.Size);
                b.CopyTo(vertices);

                var c = new ReadOnlySpan<ushort>(cmdList.IdxBuffer.Data, cmdList.IdxBuffer.Size);
                c.CopyTo(indices);
            }

            vertices = vertices[cmdList.VtxBuffer.Size..];
            indices = indices[cmdList.IdxBuffer.Size..];
        }

        vertexBuffer.Unmap();
        commandBuffer.BindVertexBuffer(vertexBuffer);

        indexBuffer.Unmap();
        commandBuffer.BindIndexBuffer(indexBuffer, IndexType.Uint16);
    }

    private static void CreatePipeline(GpuContext ctx, Format format) {
        pipeline = ctx.Pipelines.Create(new GpuGraphicsPipelineOptions(
            PrimitiveTopology.TriangleList,
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.imgui.spv"),
            GpuShaderModule.FromResource("Abyss.Engine.shaders.bin.imgui.spv"),
            new VertexFormat([
                new VertexAttribute(VertexAttributeType.Float, 2, false),
                new VertexAttribute(VertexAttributeType.Float, 2, false),
                new VertexAttribute(VertexAttributeType.UByte, 4, true)
            ]),
            [
                new ColorAttachment(format, BlendMode.Alpha),
            ],
            Layout: ctx.Pipelines.GetLayout(
                (uint) Utils.SizeOf<DrawData>(),
                ctx.Descriptors.GetLayout(DescriptorType.ImageSampler)
            )
        ));
    }

    private static void CreateGpuResources(GpuContext ctx) {
        var io = ImGui.GetIO();

        unsafe {
            var pixels = default(byte*);
            var width = 0;
            var height = 0;

            io.Fonts.GetTexDataAsRGBA32(ref pixels, ref width, ref height);

            fontImage = ctx.CreateImage(
                new Vector2D<uint>((uint) width, (uint) height),
                ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
                Format.R8G8B8A8Unorm
            );

            GpuSyncUploads.UploadToImage(new ReadOnlySpan<byte>(pixels, width * height * 4), fontImage);
        }

        sampler = ctx.CreateSampler(Filter.Linear, Filter.Linear, SamplerAddressMode.ClampToEdge);
    }

    private static ImGuiKey? ConvertKey(Key key) => key switch {
        Key.Space => ImGuiKey.Space,
        Key.Apostrophe => ImGuiKey.Apostrophe,
        Key.Comma => ImGuiKey.Comma,
        Key.Minus => ImGuiKey.Minus,
        Key.Period => ImGuiKey.Period,
        Key.Slash => ImGuiKey.Slash,
        Key.Number0 => ImGuiKey.Key0,
        Key.Number1 => ImGuiKey.Key1,
        Key.Number2 => ImGuiKey.Key2,
        Key.Number3 => ImGuiKey.Key3,
        Key.Number4 => ImGuiKey.Key4,
        Key.Number5 => ImGuiKey.Key5,
        Key.Number6 => ImGuiKey.Key6,
        Key.Number7 => ImGuiKey.Key7,
        Key.Number8 => ImGuiKey.Key8,
        Key.Number9 => ImGuiKey.Key9,
        Key.Semicolon => ImGuiKey.Semicolon,
        Key.Equal => ImGuiKey.Equal,
        Key.A => ImGuiKey.A,
        Key.B => ImGuiKey.B,
        Key.C => ImGuiKey.C,
        Key.D => ImGuiKey.D,
        Key.E => ImGuiKey.E,
        Key.F => ImGuiKey.F,
        Key.G => ImGuiKey.G,
        Key.H => ImGuiKey.H,
        Key.I => ImGuiKey.I,
        Key.J => ImGuiKey.J,
        Key.K => ImGuiKey.K,
        Key.L => ImGuiKey.L,
        Key.M => ImGuiKey.M,
        Key.N => ImGuiKey.N,
        Key.O => ImGuiKey.O,
        Key.P => ImGuiKey.P,
        Key.Q => ImGuiKey.Q,
        Key.R => ImGuiKey.R,
        Key.S => ImGuiKey.S,
        Key.T => ImGuiKey.T,
        Key.U => ImGuiKey.U,
        Key.V => ImGuiKey.V,
        Key.W => ImGuiKey.W,
        Key.X => ImGuiKey.X,
        Key.Y => ImGuiKey.Y,
        Key.Z => ImGuiKey.Z,
        Key.LeftBracket => ImGuiKey.LeftBracket,
        Key.BackSlash => ImGuiKey.Backslash,
        Key.RightBracket => ImGuiKey.RightBracket,
        Key.GraveAccent => ImGuiKey.GraveAccent,
        Key.Escape => ImGuiKey.Escape,
        Key.Enter => ImGuiKey.Enter,
        Key.Tab => ImGuiKey.Tab,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Right => ImGuiKey.RightArrow,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.PageUp => ImGuiKey.PageUp,
        Key.PageDown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.CapsLock => ImGuiKey.CapsLock,
        Key.ScrollLock => ImGuiKey.ScrollLock,
        Key.NumLock => ImGuiKey.NumLock,
        Key.PrintScreen => ImGuiKey.PrintScreen,
        Key.Pause => ImGuiKey.Pause,
        Key.F1 => ImGuiKey.F1,
        Key.F2 => ImGuiKey.F2,
        Key.F3 => ImGuiKey.F3,
        Key.F4 => ImGuiKey.F4,
        Key.F5 => ImGuiKey.F5,
        Key.F6 => ImGuiKey.F6,
        Key.F7 => ImGuiKey.F7,
        Key.F8 => ImGuiKey.F8,
        Key.F9 => ImGuiKey.F9,
        Key.F10 => ImGuiKey.F10,
        Key.F11 => ImGuiKey.F11,
        Key.F12 => ImGuiKey.F12,
        Key.F13 => ImGuiKey.F13,
        Key.F14 => ImGuiKey.F14,
        Key.F15 => ImGuiKey.F15,
        Key.F16 => ImGuiKey.F16,
        Key.F17 => ImGuiKey.F17,
        Key.F18 => ImGuiKey.F18,
        Key.F19 => ImGuiKey.F19,
        Key.F20 => ImGuiKey.F20,
        Key.F21 => ImGuiKey.F21,
        Key.F22 => ImGuiKey.F22,
        Key.F23 => ImGuiKey.F23,
        Key.F24 => ImGuiKey.F24,
        Key.Keypad0 => ImGuiKey.Keypad0,
        Key.Keypad1 => ImGuiKey.Keypad1,
        Key.Keypad2 => ImGuiKey.Keypad2,
        Key.Keypad3 => ImGuiKey.Keypad3,
        Key.Keypad4 => ImGuiKey.Keypad4,
        Key.Keypad5 => ImGuiKey.Keypad5,
        Key.Keypad6 => ImGuiKey.Keypad6,
        Key.Keypad7 => ImGuiKey.Keypad7,
        Key.Keypad8 => ImGuiKey.Keypad8,
        Key.Keypad9 => ImGuiKey.Keypad9,
        Key.KeypadDecimal => ImGuiKey.KeypadDecimal,
        Key.KeypadDivide => ImGuiKey.KeypadDivide,
        Key.KeypadMultiply => ImGuiKey.KeypadMultiply,
        Key.KeypadSubtract => ImGuiKey.KeypadSubtract,
        Key.KeypadAdd => ImGuiKey.KeypadAdd,
        Key.KeypadEnter => ImGuiKey.KeypadEnter,
        Key.KeypadEqual => ImGuiKey.KeypadEqual,
        Key.ShiftLeft => ImGuiKey.LeftShift,
        Key.ControlLeft => ImGuiKey.LeftCtrl,
        Key.AltLeft => ImGuiKey.LeftAlt,
        Key.SuperLeft => ImGuiKey.LeftSuper,
        Key.ShiftRight => ImGuiKey.RightShift,
        Key.ControlRight => ImGuiKey.RightCtrl,
        Key.AltRight => ImGuiKey.RightAlt,
        Key.SuperRight => ImGuiKey.RightSuper,
        Key.Menu => ImGuiKey.Menu,
        _ => null
    };
}