using Abyss.Core;
using Abyss.Gpu.Spirv;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace Abyss.Gpu.Pipeline;

public class GpuPipelineManager {
    private readonly GpuContext ctx;

    private readonly MultiKeyDictionary<DescriptorSetLayout, PipelineLayout> layouts = new();

    public GpuPipelineManager(GpuContext ctx) {
        this.ctx = ctx;
    }

    public unsafe PipelineLayout GetLayout(uint pushConstantsSize, params ReadOnlySpan<DescriptorSetLayout> setLayouts) {
        if (!layouts.TryGetValue(setLayouts, out var layout)) {
            var pushConstantsInfo = new PushConstantRange(
                stageFlags: ShaderStageFlags.All,
                size: pushConstantsSize
            );

            VkUtils.Wrap(ctx.Vk.CreatePipelineLayout(ctx.Device, new PipelineLayoutCreateInfo(
                pushConstantRangeCount: pushConstantsSize > 0 ? 1u : 0u,
                pPushConstantRanges: &pushConstantsInfo,
                setLayoutCount: (uint) setLayouts.Length,
                pSetLayouts: Utils.AsPtr(setLayouts)
            ), null, out layout), "Failed to create a Pipeline Layout");

            layouts[setLayouts] = layout;
        }

        return layout;
    }

    private PipelineLayout GetLayout(ShaderModuleInfo[] shaderInfos) {
        var setInfos = MergeBindings(shaderInfos);
        Span<DescriptorSetLayout> setLayouts = stackalloc DescriptorSetLayout[setInfos.Length];

        for (var i = 0; i < setInfos.Length; i++) {
            var infos = setInfos[i];

            if (infos != null)
                setLayouts[i] = ctx.Descriptors.GetLayout(infos);
        }

        return GetLayout(0, setLayouts);
    }

    public unsafe GpuGraphicsPipeline Create(GpuGraphicsPipelineOptions options) {
        var shaderInfos = new Dictionary<GpuShaderModule, ShaderModuleInfo>();

        CreateShaderModuleInfo(options.VertexShader, shaderInfos);
        CreateShaderModuleInfo(options.FragmentShader, shaderInfos);

        Span<PipelineShaderStageCreateInfo> shaderStages = [
            CreateShaderStage(shaderInfos[options.VertexShader], ShaderStageFlags.VertexBit),
            CreateShaderStage(shaderInfos[options.FragmentShader], ShaderStageFlags.FragmentBit)
        ];

        Span<VertexInputBindingDescription> vertexBindings =
            stackalloc VertexInputBindingDescription[options.Format.Attributes.Length == 0 ? 0 : 1];

        if (vertexBindings.Length > 0)
            vertexBindings[0] = new VertexInputBindingDescription(
                0,
                options.Format.Stride,
                VertexInputRate.Vertex
            );

        Span<VertexInputAttributeDescription> vertexAttributes =
            stackalloc VertexInputAttributeDescription[options.Format.Attributes.Length];

        var vertexAttributeOffset = 0u;

        for (var i = 0; i < vertexAttributes.Length; i++) {
            var attribute = options.Format.Attributes[i];

            vertexAttributes[i] = new VertexInputAttributeDescription(
                binding: 0,
                location: (uint) i,
                format: attribute.Format,
                offset: vertexAttributeOffset
            );

            vertexAttributeOffset += attribute.Size;
        }

        var vertexInfo = new PipelineVertexInputStateCreateInfo(
            vertexBindingDescriptionCount: (uint) vertexBindings.Length,
            pVertexBindingDescriptions: Utils.AsPtr(vertexBindings),
            vertexAttributeDescriptionCount: (uint) vertexAttributes.Length,
            pVertexAttributeDescriptions: Utils.AsPtr(vertexAttributes)
        );

        var inputAssemblyInfo = new PipelineInputAssemblyStateCreateInfo(
            topology: options.Topology
        );

        var viewportInfo = new PipelineViewportStateCreateInfo(
            viewportCount: 1,
            scissorCount: 1
        );

        var rasterizationInfo = new PipelineRasterizationStateCreateInfo(
            polygonMode: PolygonMode.Fill,
            cullMode: CullModeFlags.None,
            frontFace: FrontFace.CounterClockwise,
            lineWidth: 1
        );

        var multisampleInfo = new PipelineMultisampleStateCreateInfo(
            rasterizationSamples: SampleCountFlags.Count1Bit
        );

        var depthStencilInfo = new PipelineDepthStencilStateCreateInfo(
            depthTestEnable: options.DepthAttachment != null,
            depthWriteEnable: options.DepthAttachment?.Write ?? false,
            depthCompareOp: options.DepthAttachment?.Compare ?? CompareOp.Less
        );

        Span<PipelineColorBlendAttachmentState> colorAttachmentBlends =
            stackalloc PipelineColorBlendAttachmentState[options.ColorAttachments.Length];

        for (var i = 0; i < colorAttachmentBlends.Length; i++) {
            colorAttachmentBlends[i] = new PipelineColorBlendAttachmentState(
                options.ColorAttachments[i].Blend != null,
                colorWriteMask: ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit
            );

            if (options.ColorAttachments[i].Blend != null) {
                var mode = options.ColorAttachments[i].Blend!.Value;

                colorAttachmentBlends[i].SrcColorBlendFactor = mode.SrcColor;
                colorAttachmentBlends[i].DstColorBlendFactor = mode.DstColor;
                colorAttachmentBlends[i].SrcAlphaBlendFactor = mode.SrcAlpha;
                colorAttachmentBlends[i].DstAlphaBlendFactor = mode.DstAlpha;
            }
        }

        var colorBlendInfo = new PipelineColorBlendStateCreateInfo(
            attachmentCount: (uint) colorAttachmentBlends.Length,
            pAttachments: Utils.AsPtr(colorAttachmentBlends)
        );

        Span<DynamicState> dynamicStates = [
            DynamicState.Viewport,
            DynamicState.Scissor
        ];

        var dynamicInfo = new PipelineDynamicStateCreateInfo(
            dynamicStateCount: (uint) dynamicStates.Length,
            pDynamicStates: Utils.AsPtr(dynamicStates)
        );

        Span<Format> colorAttachmentFormats = stackalloc Format[options.ColorAttachments.Length];

        for (var i = 0; i < colorAttachmentFormats.Length; i++)
            colorAttachmentFormats[i] = options.ColorAttachments[i].Format;

        var renderingInfo = new PipelineRenderingCreateInfo(
            colorAttachmentCount: (uint) colorAttachmentFormats.Length,
            pColorAttachmentFormats: Utils.AsPtr(colorAttachmentFormats),
            depthAttachmentFormat: options.DepthAttachment?.Format ?? Format.Undefined
        );

        var layout = options.Layout ?? GetLayout(shaderInfos.Values.ToArray());

        VkUtils.Wrap(ctx.Vk.CreateGraphicsPipelines(ctx.Device, new PipelineCache(), 1, new GraphicsPipelineCreateInfo(
            pNext: &renderingInfo,
            stageCount: (uint) shaderStages.Length,
            pStages: Utils.AsPtr(shaderStages),
            pVertexInputState: &vertexInfo,
            pInputAssemblyState: &inputAssemblyInfo,
            pViewportState: &viewportInfo,
            pRasterizationState: &rasterizationInfo,
            pMultisampleState: &multisampleInfo,
            pDepthStencilState: &depthStencilInfo,
            pColorBlendState: &colorBlendInfo,
            pDynamicState: &dynamicInfo,
            layout: layout
        ), null, out var handle), "Failed to create a Graphics Pipeline");

        foreach (var shaderStage in shaderStages)
            SilkMarshal.FreeString((IntPtr) shaderStage.PName);

        foreach (var info in shaderInfos.Values)
            ctx.Vk.DestroyShaderModule(ctx.Device, info.Handle, null);

        return new GpuGraphicsPipeline(ctx, layout, handle, options);
    }

    public unsafe GpuRayTracePipeline Create(GpuRayTracePipelineOptions options) {
        var shaderInfos = new Dictionary<GpuShaderModule, ShaderModuleInfo>();

        foreach (var shaderModule in options.ShaderModules)
            CreateShaderModuleInfo(shaderModule, shaderInfos);

        Span<PipelineShaderStageCreateInfo> shaderStages = stackalloc PipelineShaderStageCreateInfo[options.ShaderModules.Length];

        for (var i = 0; i < shaderStages.Length; i++) {
            var info = shaderInfos[options.ShaderModules[i]];
            shaderStages[i] = CreateShaderStage(info, info.Spirv.EntryPoints[0].Stage);
        }

        Span<RayTracingShaderGroupCreateInfoKHR> shaderGroups = stackalloc RayTracingShaderGroupCreateInfoKHR[options.ShaderGroups.Length];

        for (var i = 0; i < shaderGroups.Length; i++) {
            var group = options.ShaderGroups[i];

            shaderGroups[i] = new RayTracingShaderGroupCreateInfoKHR(
                type: group.Type,
                generalShader: group.GeneralI ?? Vk.ShaderUnusedKhr,
                intersectionShader: group.IntersectionI ?? Vk.ShaderUnusedKhr,
                anyHitShader: group.AnyHitI ?? Vk.ShaderUnusedKhr,
                closestHitShader: group.ClosestHitI ?? Vk.ShaderUnusedKhr
            );
        }

        var layout = options.Layout ?? GetLayout(shaderInfos.Values.ToArray());

        VkUtils.Wrap(ctx.RayTracingApi.CreateRayTracingPipelines(ctx.Device, new DeferredOperationKHR(), new PipelineCache(), 1,
            new RayTracingPipelineCreateInfoKHR(
                stageCount: (uint) shaderStages.Length,
                pStages: Utils.AsPtr(shaderStages),
                groupCount: (uint) shaderGroups.Length,
                pGroups: Utils.AsPtr(shaderGroups),
                maxPipelineRayRecursionDepth: options.MaxRecursionDepth,
                layout: layout
            ), null, out var handle), "Failed to create a Ray Trace Pipeline");

        foreach (var shaderStage in shaderStages)
            SilkMarshal.FreeString((IntPtr) shaderStage.PName);

        foreach (var info in shaderInfos.Values)
            ctx.Vk.DestroyShaderModule(ctx.Device, info.Handle, null);

        return new GpuRayTracePipeline(ctx, layout, handle, options);
    }

    private static DescriptorInfo?[]?[] MergeBindings(ShaderModuleInfo[] shaderInfos) {
        var setCount = shaderInfos
            .SelectMany(info => info.Spirv.Bindings)
            .Select(binding => binding.Set)
            .Max() + 1;

        var setInfos = new DescriptorInfo?[]?[setCount];

        for (var i = 0; i < setCount; i++) {
            var setI = i;

            var count = shaderInfos
                .SelectMany(info => info.Spirv.Bindings)
                .Where(binding => binding.Set == setI)
                .Select(binding => binding.Index)
                .Max() + 1;

            var infos = new DescriptorInfo?[count];

            foreach (var binding in shaderInfos.SelectMany(info => info.Spirv.Bindings).Where(binding => binding.Set == setI)) {
                if (infos[binding.Index] != null && infos[binding.Index] != binding.Info)
                    throw new Exception("Incompatible descriptor type between shaders");

                infos[binding.Index] = binding.Info;
            }

            setInfos[setI] = infos;
        }

        return setInfos;
    }

    private unsafe void CreateShaderModuleInfo(GpuShaderModule shaderModule, Dictionary<GpuShaderModule, ShaderModuleInfo> infos) {
        if (infos.ContainsKey(shaderModule))
            return;

        var bytes = shaderModule.Read();
        var info = SpirvParser.Parse(bytes);

        VkUtils.Wrap(ctx.Vk.CreateShaderModule(ctx.Device, new ShaderModuleCreateInfo(
            codeSize: (uint) bytes.Length,
            pCode: (uint*) Utils.AsPtr(bytes)
        ), null, out var module), "Failed to create a Shader Module");

        infos[shaderModule] = new ShaderModuleInfo(module, info);
    }

    private static unsafe PipelineShaderStageCreateInfo CreateShaderStage(ShaderModuleInfo info, ShaderStageFlags stage) {
        var entryPoint = info.Spirv.EntryPoints.FirstNullable(entryPoint => entryPoint.Stage == stage);
        if (entryPoint == null) throw new Exception("Shader module doesn't have a suitable entry point");

        return new PipelineShaderStageCreateInfo(
            stage: stage,
            module: info.Handle,
            pName: (byte*) SilkMarshal.StringToPtr(entryPoint.Value.Name)
        );
    }

    private readonly record struct ShaderModuleInfo(ShaderModule Handle, SpirvInfo Spirv);
}