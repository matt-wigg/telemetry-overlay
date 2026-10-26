namespace Telemetry.Core;

public sealed record FrameWindow(FrameStatistics? Statistics, double[] Graph, string? SwapChain)
{
    /// <summary>Rebuild by event time, allowing late ETW delivery to fill historical graph buckets.</summary>
    public static FrameWindow Calculate(IEnumerable<FrameSample> source, double nowMs, int processId)
    {
        var frames = source.Where(f => f.ProcessId == processId && double.IsFinite(f.TimestampMs) && double.IsFinite(f.DurationMs)
            && f.DurationMs > 0 && f.TimestampMs <= nowMs && f.TimestampMs > nowMs - 30_000).ToArray();
        var recent = frames.Where(f => f.TimestampMs > nowMs - 10_000).ToArray();
        var primary = recent.GroupBy(f => f.SwapChain).OrderByDescending(g => g.Count()).FirstOrDefault();
        var statistics = primary is not null && nowMs - primary.Max(f => f.TimestampMs) < 2500
            ? FrameStatistics.Calculate(primary.Select(f => f.DurationMs)) : null;
        var graph = Enumerable.Repeat(double.NaN, 60).ToArray();
        if (primary is not null)
        {
            foreach (var frame in frames.Where(f => f.SwapChain == primary.Key))
            {
                int bucket = Math.Clamp((int)((frame.TimestampMs - (nowMs - 30_000)) / 500), 0, 59);
                graph[bucket] = double.IsNaN(graph[bucket]) ? frame.DurationMs : Math.Max(graph[bucket], frame.DurationMs);
            }
        }
        return new(statistics, graph, primary?.Key);
    }
}
