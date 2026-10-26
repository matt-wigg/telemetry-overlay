namespace Telemetry.Core;

/// <summary>Pairs successful Present calls per runtime/thread, measures intervals per swap chain.
/// Single-owner component; caller serializes access. Bounds prevent untrusted event streams growing memory.</summary>
public sealed class PresentAccumulator(double timestampFrequency)
{
    private readonly double _millisecondsPerTick = timestampFrequency > 0 && double.IsFinite(timestampFrequency)
        ? 1000.0 / timestampFrequency : throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
    private readonly Dictionary<(bool Dxgi, uint Thread), (ulong Chain, long Start)> _inFlight = [];
    private readonly Dictionary<(bool Dxgi, ulong Chain), long> _previous = [];
    public long RejectedForCapacity { get; private set; }

    public void Clear() { _inFlight.Clear(); _previous.Clear(); }

    public void Start(bool dxgi, uint thread, ulong chain, long timestamp, bool testOnly)
    {
        var key = (dxgi, thread);
        _inFlight.Remove(key);
        if (testOnly || timestamp < 0) return;
        if (_inFlight.Count >= 256) { RejectedForCapacity++; return; }
        _inFlight[key] = (chain, timestamp);
    }

    public FrameSample? Stop(bool dxgi, uint thread, int processId, int result)
    {
        if (!_inFlight.Remove((dxgi, thread), out var current) || result != 0) return null;
        var key = (dxgi, current.Chain);
        if (!_previous.TryGetValue(key, out long previous))
        {
            if (_previous.Count >= 64) { RejectedForCapacity++; return null; }
            _previous[key] = current.Start; return null;
        }
        if (current.Start <= previous) return null;
        _previous[key] = current.Start;
        return new(processId, current.Chain.ToString("X"), current.Start * _millisecondsPerTick,
            (current.Start - previous) * _millisecondsPerTick, null, null, "ApplicationPresent");
    }
}
