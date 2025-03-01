using System.Collections.Immutable;
using Abyss.Core;
using Abyss.Gpu.Pipeline;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using VMASharp;

namespace Abyss.Gpu;

public class GpuContext {
    public readonly VulkanMemoryAllocator Allocator;
    public readonly GpuCommandPool CommandPool;
    public readonly GpuDescriptorManager Descriptors;
    public readonly FrameAllocator FrameAllocator;
    public readonly GpuPipelineManager Pipelines;
    public readonly GpuQueryManager Queries;

    private readonly Fence runFence;

    public readonly GpuSurface Surface;
    public readonly GpuSwapchain Swapchain;
    public readonly Vk Vk;
    public readonly IVkSurface VkSurface;
    public KhrAccelerationStructure AccelStructApi = null!;
    public ExtDebugUtils DebugUtilsApi = null!;
    public KhrDeferredHostOperations DeferredApi = null!;
    public Device Device;

    public Instance Instance;
    public PhysicalDevice PhysicalDevice;
    public Queue Queue;
    public KhrRayTracingPipeline RayTracingApi = null!;

    public GpuContext(IWindow window) {
        Vk = Vk.GetApi();
        VkSurface = window.VkSurface!;

        CreateInstance();
        SelectPhysicalDevice();
        CreateDevice();

        Surface = new GpuSurface(this);
        Swapchain = new GpuSwapchain(this, window);

        /*unsafe {
            debugUtilsApi.CreateDebugUtilsMessenger(Instance, new DebugUtilsMessengerCreateInfoEXT(
                messageSeverity: DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt | DebugUtilsMessageSeverityFlagsEXT.InfoBitExt | DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt | DebugUtilsMessageSeverityFlagsEXT.WarningBitExt,
                messageType: DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt | DebugUtilsMessageTypeFlagsEXT.DeviceAddressBindingBitExt,
                pfnUserCallback: new PfnDebugUtilsMessengerCallbackEXT((severity, types, data, userData) => {
                    Console.WriteLine(SilkMarshal.PtrToString((IntPtr) data->PMessage));
                    return 1;
                })
            ), null, out _);
        }*/

        Allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo(
            Vk.Version13,
            Vk,
            Instance,
            PhysicalDevice,
            Device,
            AllocatorCreateFlags.BufferDeviceAddress
        ));

        Descriptors = new GpuDescriptorManager(this);
        Pipelines = new GpuPipelineManager(this);
        CommandPool = new GpuCommandPool(this);
        Queries = new GpuQueryManager(this);
        FrameAllocator = new FrameAllocator(this);

        unsafe {
            Vk.CreateFence(Device, new FenceCreateInfo(pNext: null), null, out runFence);
        }
    }

    public void NewFrame() {
        Queries.NewFrame();
        FrameAllocator.NewFrame();
    }

    internal void OnDestroyResource(GpuResource resource) {
        Descriptors.OnDestroyResource(resource);
    }

    public unsafe GpuAccelStruct CreateAccelStruct<T>(ReadOnlySpan<T> primitives, bool bottomLevel, ref GpuBuffer? scratchBuffer)
        where T : unmanaged {
        var primitiveBuffer = CreateBuffer(
            (ulong) primitives.Length * Utils.SizeOf<T>() + 16u,
            BufferUsageFlags.AccelerationStructureBuildInputReadOnlyBitKhr | BufferUsageFlags.ShaderDeviceAddressBit |
            BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryUsage.GPU_Only
        );

        var primitiveBufferAddress = Utils.Align(primitiveBuffer.DeviceAddress, 16u);
        var offset = primitiveBufferAddress - primitiveBuffer.DeviceAddress;

        GpuSyncUploads.UploadToBuffer(primitives, primitiveBuffer.Sub(offset, (ulong) primitives.Length * Utils.SizeOf<T>()));

        var geometry = new AccelerationStructureGeometryKHR(
            geometryType: bottomLevel ? GeometryTypeKHR.AabbsKhr : GeometryTypeKHR.InstancesKhr,
            geometry: new AccelerationStructureGeometryDataKHR(
                aabbs: bottomLevel
                    ? new AccelerationStructureGeometryAabbsDataKHR(
                        data: new DeviceOrHostAddressConstKHR(primitiveBufferAddress),
                        stride: Utils.SizeOf<T>()
                    )
                    : null,
                instances: !bottomLevel
                    ? new AccelerationStructureGeometryInstancesDataKHR(
                        data: new DeviceOrHostAddressConstKHR(primitiveBufferAddress)
                    )
                    : null
            ),
            flags: GeometryFlagsKHR.OpaqueBitKhr
        );

        var buildInfo = new AccelerationStructureBuildGeometryInfoKHR(
            type: bottomLevel ? AccelerationStructureTypeKHR.BottomLevelKhr : AccelerationStructureTypeKHR.TopLevelKhr,
            flags: BuildAccelerationStructureFlagsKHR.PreferFastTraceBitKhr,
            mode: BuildAccelerationStructureModeKHR.BuildKhr,
            geometryCount: 1,
            pGeometries: &geometry
        );

        if (bottomLevel) buildInfo.Flags |= BuildAccelerationStructureFlagsKHR.AllowCompactionBitKhr;

        var sizes = AccelStructApi.GetAccelerationStructureBuildSizes(
            Device,
            AccelerationStructureBuildTypeKHR.DeviceKhr,
            buildInfo,
            (uint) primitives.Length
        );

        var accelStructBuffer = CreateBuffer(
            sizes.AccelerationStructureSize,
            BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
            MemoryUsage.GPU_Only
        );

        AccelStructApi.CreateAccelerationStructure(Device, new AccelerationStructureCreateInfoKHR(
            buffer: accelStructBuffer,
            size: sizes.AccelerationStructureSize,
            type: buildInfo.Type
        ), null, out var accelStruct);

        buildInfo.DstAccelerationStructure = accelStruct;

        var accelStructProperties = new PhysicalDeviceAccelerationStructurePropertiesKHR(pNext: null);
        var properties = new PhysicalDeviceProperties2(pNext: &accelStructProperties);
        Vk.GetPhysicalDeviceProperties2(PhysicalDevice, &properties);

        var scratchSize = sizes.BuildScratchSize + accelStructProperties.MinAccelerationStructureScratchOffsetAlignment;

        if (scratchBuffer == null || scratchBuffer.Size < scratchSize) {
            scratchBuffer?.Dispose();

            scratchBuffer = CreateBuffer(
                scratchSize,
                BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryUsage.GPU_Only
            );
        }

        buildInfo.ScratchData = new DeviceOrHostAddressKHR(
            Utils.Align(scratchBuffer.DeviceAddress, accelStructProperties.MinAccelerationStructureScratchOffsetAlignment)
        );

        var buildRange = new AccelerationStructureBuildRangeInfoKHR(
            (uint) primitives.Length
        );

        var queryPool = default(QueryPool);

        if (bottomLevel) {
            Vk.CreateQueryPool(Device, new QueryPoolCreateInfo(
                queryType: QueryType.AccelerationStructureCompactedSizeKhr,
                queryCount: 1
            ), null, out queryPool);

            Vk.ResetQueryPool(Device, queryPool, 0, 1);
        }

        Run(commandBuffer => {
            var buildRange2 = buildRange;
            AccelStructApi.CmdBuildAccelerationStructures(commandBuffer, 1, buildInfo, &buildRange2);

            if (bottomLevel) {
                Vk.CmdPipelineBarrier(
                    commandBuffer,
                    PipelineStageFlags.AccelerationStructureBuildBitKhr,
                    PipelineStageFlags.AccelerationStructureBuildBitKhr,
                    DependencyFlags.None,
                    1, new MemoryBarrier(
                        srcAccessMask: AccessFlags.AccelerationStructureWriteBitKhr,
                        dstAccessMask: AccessFlags.AccelerationStructureReadBitKhr
                    ),
                    0, null,
                    0, null
                );

                // ReSharper disable once AccessToModifiedClosure
                AccelStructApi.CmdWriteAccelerationStructuresProperties(commandBuffer, 1, accelStruct,
                    QueryType.AccelerationStructureCompactedSizeKhr, queryPool, 0);
            }
        });

        primitiveBuffer.Dispose();

        if (bottomLevel) {
            ulong compactedSize = 0;
            Vk.GetQueryPoolResults(Device, queryPool, 0, 1, sizeof(ulong), ref compactedSize, sizeof(ulong),
                QueryResultFlags.ResultWaitBit);

            var compactedAccelStructBuffer = CreateBuffer(
                compactedSize,
                BufferUsageFlags.AccelerationStructureStorageBitKhr | BufferUsageFlags.ShaderDeviceAddressBit,
                MemoryUsage.GPU_Only
            );

            AccelStructApi.CreateAccelerationStructure(Device, new AccelerationStructureCreateInfoKHR(
                buffer: compactedAccelStructBuffer,
                size: compactedSize,
                type: buildInfo.Type
            ), null, out var compactedAccelStruct);

            Run(commandBuffer => {
                AccelStructApi.CmdCopyAccelerationStructure(commandBuffer, new CopyAccelerationStructureInfoKHR(
                    // ReSharper disable once AccessToModifiedClosure
                    src: accelStruct,
                    dst: compactedAccelStruct,
                    mode: CopyAccelerationStructureModeKHR.CompactKhr
                ));
            });

            AccelStructApi.DestroyAccelerationStructure(Device, accelStruct, null);
            accelStructBuffer.Dispose();

            accelStruct = compactedAccelStruct;
            accelStructBuffer = compactedAccelStructBuffer;
        }

        return new GpuAccelStruct(this, accelStruct, accelStructBuffer);
    }

    public GpuAccelStruct CreateAccelStruct<T>(Span<T> primitives, bool bottomLevel, ref GpuBuffer? scratchBuffer) where T : unmanaged {
        return CreateAccelStruct((ReadOnlySpan<T>) primitives, bottomLevel, ref scratchBuffer);
    }

    public unsafe GpuBuffer CreateBuffer(ulong size, BufferUsageFlags bufferUsage, MemoryUsage memoryUsage) {
        var memoryTypeBits = 0u;

        if (bufferUsage.HasFlag(BufferUsageFlags.ShaderDeviceAddressBit))
            if (memoryUsage != MemoryUsage.GPU_Only)
                throw new Exception("Invalid buffer and memory usage combination");
        //memoryTypeBits |= (uint) MemoryAllocateFlags.AddressBit;
        var buffer = Allocator.CreateBuffer(
            new BufferCreateInfo(
                size: size,
                usage: bufferUsage
            ),
            new AllocationCreateInfo(
                usage: memoryUsage,
                memoryTypeBits: memoryTypeBits
            ),
            out var allocation
        );

        return new GpuBuffer(this, buffer, size, bufferUsage, allocation);
    }

    public GpuBuffer CreateStaticBuffer<T>(ReadOnlySpan<T> data, BufferUsageFlags usage) where T : unmanaged {
        var buffer = CreateBuffer(
            (ulong) data.Length * Utils.SizeOf<T>(),
            usage | BufferUsageFlags.TransferDstBit,
            MemoryUsage.GPU_Only
        );

        GpuSyncUploads.UploadToBuffer(data, buffer);

        return buffer;
    }

    public GpuBuffer CreateStaticBuffer<T>(Span<T> data, BufferUsageFlags usage) where T : unmanaged {
        return CreateStaticBuffer((ReadOnlySpan<T>) data, usage);
    }

    public unsafe GpuImage CreateImage(Vector2D<uint> size, ImageUsageFlags usage, Format format) {
        var image = Allocator.CreateImage(
            new ImageCreateInfo(
                imageType: ImageType.Type2D,
                format: format,
                extent: new Extent3D(size.X, size.Y, 1),
                mipLevels: 1,
                arrayLayers: 1,
                samples: SampleCountFlags.Count1Bit,
                tiling: ImageTiling.Optimal,
                usage: usage
            ),
            new AllocationCreateInfo(
                usage: MemoryUsage.GPU_Only
            ),
            out var allocation
        );

        return new GpuImage(this, image, size, usage, format, allocation);
    }

    public unsafe Sampler CreateSampler(Filter mag, Filter min, SamplerAddressMode wrap) {
        VkUtils.Wrap(Vk.CreateSampler(Device, new SamplerCreateInfo(
            magFilter: mag,
            minFilter: min,
            addressModeU: wrap,
            addressModeV: wrap,
            addressModeW: wrap
        ), null, out var sampler), "Failed to create a Sampler");

        return sampler;
    }

    public unsafe void Run(Action<GpuCommandBuffer> fn) {
        var commandBuffer = CommandPool.Get();

        commandBuffer.Begin();
        fn(commandBuffer);
        commandBuffer.End();

        fixed (CommandBuffer* handle = &commandBuffer.Handle) {
            Vk.QueueSubmit(Queue, 1, new SubmitInfo(
                commandBufferCount: 1,
                pCommandBuffers: handle
            ), runFence);
        }

        Vk.WaitForFences(Device, 1, runFence, true, ulong.MaxValue);
        Vk.ResetFences(Device, 1, runFence);
    }

    // Init

    private unsafe void CreateInstance() {
        var appInfo = new ApplicationInfo(
            pApplicationName: (byte*) SilkMarshal.StringToPtr("Voxol"),
            applicationVersion: new Version32(1, 0, 0),
            pEngineName: (byte*) SilkMarshal.StringToPtr("No Engine"),
            engineVersion: new Version32(1, 0, 0),
            apiVersion: Vk.Version13
        );

        var layerPropertyCount = 0u;
        Vk.EnumerateInstanceLayerProperties(ref layerPropertyCount, null);

        Span<LayerProperties> layerProperties = stackalloc LayerProperties[(int) layerPropertyCount];
        Vk.EnumerateInstanceLayerProperties(ref layerPropertyCount, Utils.AsPtr(layerProperties));

        var layers = new List<string>();

        foreach (var layerProperty in layerProperties) {
            var name = SilkMarshal.PtrToString((IntPtr) layerProperty.LayerName);

#if DEBUG
            if (name == "VK_LAYER_KHRONOS_validation") {
                Console.WriteLine("Validation: enabled");
                layers.Add(name);
            }
#endif
        }

        var enabledLayers = stackalloc byte*[layers.Count];

        for (var i = 0; i < layers.Count; i++) enabledLayers[i] = (byte*) SilkMarshal.StringToPtr(layers[i]);

        var requiredExtensions = VkSurface.GetRequiredExtensions(out var requiredExtensionCount);
        var enabledExtensions = stackalloc byte*[(int) requiredExtensionCount + 2];

        enabledExtensions[0] = (byte*) SilkMarshal.StringToPtr(KhrSurface.ExtensionName);
        enabledExtensions[1] = (byte*) SilkMarshal.StringToPtr(ExtDebugUtils.ExtensionName);

        for (var i = 0; i < requiredExtensionCount; i++) enabledExtensions[i + 2] = requiredExtensions[i];

        Span<ValidationFeatureEnableEXT> b =
            [ValidationFeatureEnableEXT.BestPracticesExt, ValidationFeatureEnableEXT.SynchronizationValidationExt];

        var a = new ValidationFeaturesEXT(
            enabledValidationFeatureCount: (uint) b.Length,
            pEnabledValidationFeatures: Utils.AsPtr(b)
        );

        VkUtils.Wrap(
            Vk.CreateInstance(new InstanceCreateInfo(
                pNext: &a,
                pApplicationInfo: &appInfo,
                enabledLayerCount: (uint) layers.Count,
                ppEnabledLayerNames: enabledLayers,
                enabledExtensionCount: requiredExtensionCount + 2,
                ppEnabledExtensionNames: enabledExtensions
            ), null, out Instance),
            "Failed to create instance"
        );

        SilkMarshal.FreeString((IntPtr) enabledExtensions[0]);
        SilkMarshal.FreeString((IntPtr) enabledExtensions[1]);

        for (var i = 0; i < layers.Count; i++) SilkMarshal.FreeString((IntPtr) enabledLayers[i]);

        SilkMarshal.FreeString((IntPtr) appInfo.PApplicationName);
        SilkMarshal.FreeString((IntPtr) appInfo.PEngineName);

        Vk.TryGetInstanceExtension(Instance, out DebugUtilsApi);
    }

    private unsafe void SelectPhysicalDevice() {
        var count = 0u;
        Vk.EnumeratePhysicalDevices(Instance, ref count, null);

        Span<PhysicalDevice> physicalDevices = stackalloc PhysicalDevice[(int) count];
        Vk.EnumeratePhysicalDevices(Instance, ref count, Utils.AsPtr(physicalDevices));

        var selected = physicalDevices.ToImmutableArray()
            .Select(device => (device, Vk.GetPhysicalDeviceProperties(device)))
            .Where(tuple => (Version32) tuple.Item2.ApiVersion >= new Version(1, 3, 0))
            .Where(tuple => VkUtils.GetQueueIndices(Vk, tuple.device).Valid)
            .Where(tuple => Vk.IsDeviceExtensionPresent(tuple.device, KhrSwapchain.ExtensionName))
            //.Where(tuple => Vk.IsDeviceExtensionPresent(tuple.device, KhrDeferredHostOperations.ExtensionName))
            //.Where(tuple => Vk.IsDeviceExtensionPresent(tuple.device, KhrAccelerationStructure.ExtensionName))
            //.Where(tuple => Vk.IsDeviceExtensionPresent(tuple.device, KhrRayTracingPipeline.ExtensionName))
            .OrderBy(tuple => tuple.Item2.DeviceType == PhysicalDeviceType.DiscreteGpu ? 0 : 1)
            .Select(tuple => (PhysicalDevice?) tuple.device)
            .FirstOrDefault();

        if (!selected.HasValue)
            throw new Exception("No suitable GPU found");

        var props = Vk.GetPhysicalDeviceProperties(selected.Value);
        Console.WriteLine("GPU: " + SilkMarshal.PtrToString((IntPtr) props.DeviceName));

        PhysicalDevice = selected.Value;
    }

    private unsafe void CreateDevice() {
        var queueIndices = VkUtils.GetQueueIndices(Vk, PhysicalDevice);
        var queuePriority = 1f;

        Span<DeviceQueueCreateInfo> queueInfos = [
            new(
                queueFamilyIndex: queueIndices.Graphics,
                queueCount: 1,
                pQueuePriorities: &queuePriority
            )
        ];

        var extensions = stackalloc byte*[] {
            (byte*) SilkMarshal.StringToPtr(KhrSwapchain.ExtensionName),
            //(byte*) SilkMarshal.StringToPtr(KhrDeferredHostOperations.ExtensionName),
            //(byte*) SilkMarshal.StringToPtr(KhrAccelerationStructure.ExtensionName),
            //(byte*) SilkMarshal.StringToPtr(KhrRayTracingPipeline.ExtensionName)
        };

        /*var accelerationStructureFeatures = new PhysicalDeviceAccelerationStructureFeaturesKHR(
            accelerationStructure: true
        );

        var rayTracingPipelineFeatures = new PhysicalDeviceRayTracingPipelineFeaturesKHR(
            pNext: &accelerationStructureFeatures,
            rayTracingPipeline: true
        );*/

        var features13 = new PhysicalDeviceVulkan13Features(
            //pNext: &rayTracingPipelineFeatures,
            dynamicRendering: true
        );

        var features12 = new PhysicalDeviceVulkan12Features(
            pNext: &features13,
            hostQueryReset: true,
            bufferDeviceAddress: true,
            shaderInt8: true,
            storageBuffer8BitAccess: true,
            runtimeDescriptorArray: true,
            descriptorBindingSampledImageUpdateAfterBind: true,
            descriptorBindingPartiallyBound: true
        );

        var features = new PhysicalDeviceFeatures2(
            pNext: &features12,
            features: new PhysicalDeviceFeatures()
        );

        VkUtils.Wrap(
            Vk.CreateDevice(PhysicalDevice, new DeviceCreateInfo(
                pNext: &features,
                queueCreateInfoCount: (uint) queueInfos.Length,
                pQueueCreateInfos: Utils.AsPtr(queueInfos),
                enabledExtensionCount: 1,
                ppEnabledExtensionNames: extensions
            ), null, out Device),
            "Failed to create Device"
        );

        Vk.GetDeviceQueue(Device, queueIndices.Graphics!.Value, 0, out Queue);

        /*if (!Vk.TryGetDeviceExtension(Instance, Device, out DeferredApi))
            throw new Exception("Failed to get Deferred Host Operations API");

        if (!Vk.TryGetDeviceExtension(Instance, Device, out AccelStructApi))
            throw new Exception("Failed to get Acceleration Structure API");

        if (!Vk.TryGetDeviceExtension(Instance, Device, out RayTracingApi))
            throw new Exception("Failed to get Ray Tracing Pipeline API");*/

        for (var i = 0; i < 1; i++) SilkMarshal.FreeString((IntPtr) extensions[i]);
    }
}