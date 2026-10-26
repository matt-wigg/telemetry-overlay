namespace Telemetry.Core;

public sealed record FrameWindow(FrameStatistics? Statistics, double[] Graph, string? SwapChain, double? LatestFrameAgeMs)
{
    public const double FreshnessLimitMs = 2500;
    /// <summary>Rebuild by event time, allowing late ETW delivery to fill historical graph buckets.</summary>
    public static FrameWindow Calculate(IEnumerable<FrameSample> source, double nowMs, int processId)
    {
        var frames = source.Where(f => f.ProcessId == processId && double.IsFinite(f.TimestampMs) && double.IsFinite(f.DurationMs)
            && f.DurationMs > 0 && f.TimestampMs <= nowMs && f.TimestampMs > nowMs - 30_000).ToArray();
        var recent = frames.Where(f => f.TimestampMs > nowMs - 10_000).ToArray();
        var chains = recent.GroupBy(f => f.SwapChain)
            .Select(g => new { Frames = g, Count = g.Count(), Latest = g.Max(f => f.TimestampMs) })
            .OrderByDescending(g => g.Count).ThenBy(g => g.Frames.Key, StringComparer.Ordinal).ToArray();
        // A retired, high-frame-count chain must not hide an actively presenting replacement.
        // Keep the freshness limit: don't conceal genuine pauses by indefinitely holding old FPS.
        var active = chains.FirstOrDefault(g => g.Count >= 2 && nowMs - g.Latest < FreshnessLimitMs);
        var primary = (active ?? chains.FirstOrDefault())?.Frames;
        var statistics = active is not null ? FrameStatistics.Calculate(active.Frames.Select(f => f.DurationMs)) : null;
        var graph = Enumerable.Repeat(double.NaN, 60).ToArray();
        if (primary is not null)
        {
            foreach (var frame in frames.Where(f => f.SwapChain == primary.Key))
            {
                int bucket = Math.Clamp((int)((frame.TimestampMs - (nowMs - 30_000)) / 500), 0, 59);
                graph[bucket] = double.IsNaN(graph[bucket]) ? frame.DurationMs : Math.Max(graph[bucket], frame.DurationMs);
            }
        }
        return new(statistics, graph, primary?.Key, frames.Length > 0 ? nowMs - frames.Max(f => f.TimestampMs) : null);
    }
}
