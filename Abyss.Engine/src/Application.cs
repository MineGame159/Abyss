using Abyss.Engine.Gui;
using Abyss.Engine.Render;
using Abyss.Engine.Scene;
using Abyss.Gpu;
using Arch.Core;
using Arch.System;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Abyss.Engine;

public abstract class Application {
    public readonly IWindow Window;

    public GpuContext Ctx { get; private set; } = null!;
    public World World { get; private set; } = null!;
    public Renderer Renderer { get; private set; } = null!;

    private Group<float> systems = null!;

    private Fence submitFence;
    private Semaphore acquireImageSemaphore;
    private Semaphore submitSemaphore;

    protected Application() {
        Window = Silk.NET.Windowing.Window.Create(new WindowOptions {
            Title = "Voxol",
            Size = new Vector2D<int>(1280, 720),
            IsVisible = true,
            API = new GraphicsAPI(ContextAPI.Vulkan, ContextProfile.Core, ContextFlags.Debug, new APIVersion(1, 3))
        });

        Window.Load += InitInternal;
        Window.Render += RenderInternal;
    }

    protected abstract void Init();

    protected abstract void Update(float delta);

    public void Run() {
        Window.Run();
    }

    private unsafe void InitInternal() {
        Input.Init(Window);

        Ctx = new GpuContext(Window);
        World = World.Create();
        Renderer = new Renderer(World, Ctx);

        systems = new Group<float>(
            "Abyss",
            Renderer
        );

        Ctx.Vk.CreateFence(Ctx.Device, new FenceCreateInfo(
            flags: FenceCreateFlags.SignaledBit
        ), null, out submitFence);

        Ctx.Vk.CreateSemaphore(Ctx.Device, new SemaphoreCreateInfo(flags: SemaphoreCreateFlags.None), null, out acquireImageSemaphore);
        Ctx.Vk.CreateSemaphore(Ctx.Device, new SemaphoreCreateInfo(flags: SemaphoreCreateFlags.None), null, out submitSemaphore);

        ImGuiImpl.Init(Ctx);
        AbyssGui.Init(World);

        World.Create(new Root(), new Info("<root>"));

        Init();
    }

    private unsafe void RenderInternal(double delta) {
        Ctx.Vk.WaitForFences(Ctx.Device, 1, submitFence, true, ulong.MaxValue);

        var output = Ctx.Swapchain.GetNextImage(this.acquireImageSemaphore);
        if (output == null) return;

        Ctx.Vk.ResetFences(Ctx.Device, 1, submitFence);

        Ctx.NewFrame();

        var acquireImageSemaphore = this.acquireImageSemaphore;
        var submitSemaphore = this.submitSemaphore;

        Ctx.CommandPool.Reset();

        Update((float) delta);
        WorldTransforms.Update(World);

        var commandBuffer = Ctx.CommandPool.Get();
        commandBuffer.Begin();

        Renderer.NewFrame(commandBuffer, output);
        systems.BeforeUpdate((float) delta);
        systems.Update((float) delta);
        systems.AfterUpdate((float) delta);

        ImGuiImpl.BeginFrame((float) delta);
        AbyssGui.Render();
        ImGuiImpl.EndFrame(commandBuffer, output);

        commandBuffer.TransitionImage(
            output,
            ImageLayout.PresentSrcKhr,
            PipelineStageFlags.ColorAttachmentOutputBit, AccessFlags.ColorAttachmentWriteBit,
            PipelineStageFlags.BottomOfPipeBit, AccessFlags.None
        );

        Input.Update();
        commandBuffer.End();

        var submitWaitStageMask = PipelineStageFlags.ColorAttachmentOutputBit;

        fixed (CommandBuffer* handle = &commandBuffer.Handle) {
            Ctx.Vk.QueueSubmit(Ctx.Queue, 1, new SubmitInfo(
                waitSemaphoreCount: 1,
                pWaitSemaphores: &acquireImageSemaphore,
                pWaitDstStageMask: &submitWaitStageMask,
                signalSemaphoreCount: 1,
                pSignalSemaphores: &submitSemaphore,
                commandBufferCount: 1,
                pCommandBuffers: handle
            ), submitFence);
        }

        Ctx.Swapchain.Present(output, submitSemaphore);
    }
}