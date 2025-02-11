using Silk.NET.Vulkan;

namespace Abyss.Gpu;

public class GpuQueryManager {
    private readonly GpuContext ctx;

    private readonly double nsPerTick;

    internal readonly QueryPool Pool;

    private readonly GpuQuery[] queries = new GpuQuery[16];
    private uint nextQuery;

    public GpuQueryManager(GpuContext ctx) {
        this.ctx = ctx;

        for (var i = 0; i < queries.Length; i++) queries[i] = new GpuQuery((uint) i * 2);

        unsafe {
            VkUtils.Wrap(ctx.Vk.CreateQueryPool(ctx.Device, new QueryPoolCreateInfo(
                queryType: QueryType.Timestamp,
                queryCount: (uint) queries.Length * 2,
                pipelineStatistics: QueryPipelineStatisticFlags.None
            ), null, out Pool), "Failed to create a Query Pool");
        }

        ctx.Vk.ResetQueryPool(ctx.Device, Pool, 0, (uint) queries.Length * 2);

        var properties = ctx.Vk.GetPhysicalDeviceProperties(ctx.PhysicalDevice);
        nsPerTick = properties.Limits.TimestampPeriod;
    }

    internal void NewFrame() {
        if (nextQuery == 0)
            return;

        Span<ulong> timings = stackalloc ulong[(int) nextQuery * 2];

        VkUtils.Wrap(
            ctx.Vk.GetQueryPoolResults(ctx.Device, Pool, 0, nextQuery * 2, timings, sizeof(ulong), QueryResultFlags.ResultWaitBit),
            "Failed to get query results"
        );

        for (var i = 0; i < nextQuery; i++) {
            var query = queries[i];

            var ticks = timings[(int) query.EndI] - timings[(int) query.BeginI];
            query.Time = new TimeSpan((long) (ticks * nsPerTick / TimeSpan.NanosecondsPerTick));
        }

        ctx.Vk.ResetQueryPool(ctx.Device, Pool, 0, nextQuery * 2);
        nextQuery = 0;
    }

    public GpuQuery GetNext() {
        if (nextQuery >= queries.Length)
            throw new Exception("Too many Gpu Queries");

        return queries[nextQuery++];
    }
}

public class GpuQuery {
    internal readonly uint BeginI;

    public GpuQuery(uint beginI) {
        BeginI = beginI;
    }

    internal uint EndI => BeginI + 1;

    public TimeSpan Time { get; internal set; }

    public void Begin(GpuCommandBuffer commandBuffer, PipelineStageFlags stage) {
        commandBuffer.Ctx.Vk.CmdWriteTimestamp(commandBuffer, stage, commandBuffer.Ctx.Queries.Pool, BeginI);
    }

    public void End(GpuCommandBuffer commandBuffer, PipelineStageFlags stage) {
        commandBuffer.Ctx.Vk.CmdWriteTimestamp(commandBuffer, stage, commandBuffer.Ctx.Queries.Pool, EndI);
    }
}