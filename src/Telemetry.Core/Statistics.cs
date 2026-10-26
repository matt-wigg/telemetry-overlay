namespace Telemetry.Core;

/// <summary>Fixed-capacity FIFO. Append O(1), memory O(capacity), no per-append allocation.</summary>
public sealed class RingBuffer<T>(int capacity)
{
    private readonly T[] _items = new T[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private int _next;
    public int Count { get; private set; }
    public void Add(T value) { _items[_next] = value; _next = (_next + 1) % _items.Length; Count = Math.Min(Count + 1, _items.Length); }
    public void Clear() { Array.Clear(_items); _next = 0; Count = 0; }
    public IEnumerable<T> Items()
    {
        int start = (_next - Count + _items.Length) % _items.Length;
        for (int i = 0; i < Count; i++) yield return _items[(start + i) % _items.Length];
    }
}

public sealed record FrameStatistics(int Count, double Fps, double OnePercentLow, double P99Ms, double MeanMs, double MaxMs)
{
    // Exact nearest-rank P99; 1% low is reciprocal of mean slowest ceil(N*0.01) frame times.
    public static FrameStatistics? Calculate(IEnumerable<double> durations)
    {
        var sorted = durations.Where(d => double.IsFinite(d) && d > 0).ToArray();
        if (sorted.Length < 2) return null;
        Array.Sort(sorted);
        var mean = sorted.Average();
        int tail = Math.Max(1, (int)Math.Ceiling(sorted.Length * .01));
        double tailSum = 0;
        for (int i = sorted.Length - tail; i < sorted.Length; i++) tailSum += sorted[i];
        return new(sorted.Length, 1000 / mean, 1000 / (tailSum / tail),
            sorted[(int)Math.Ceiling(sorted.Length * .99) - 1], mean, sorted[^1]);
    }
}

/// <summary>Hysteresis and persistence prevent color flicker. Heavy utilization is not an error.</summary>
public sealed class SeverityLatch
{
    private Severity _current;
    private Severity _candidate;
    private int _observations;
    public Severity Update(double? value, double warning, double critical, bool utilization = false)
    {
        if (value is null || !double.IsFinite(value.Value)) return _current = Severity.Unavailable;
        var adjusted = value.Value;
        if (_current == Severity.Critical) critical -= 2;
        if (_current == Severity.Warning) warning -= 2;
        var next = adjusted >= critical ? Severity.Critical : adjusted >= warning ? Severity.Warning : Severity.Normal;
        if (utilization) next = adjusted >= 90 ? Severity.Busy : Severity.Normal;
        if (next != _candidate) { _candidate = next; _observations = 1; }
        else _observations++;
        if (_observations >= 3 || _current == Severity.Unavailable) _current = next;
        return _current;
    }
}
