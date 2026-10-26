using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace Telemetry.App;

internal sealed class CaptureWriter : IDisposable
{
    private readonly StreamWriter _frames;
    private readonly StreamWriter _sensors;
    private readonly Dictionary<string, List<double>> _durations = [];
    private readonly object _gate = new();
    private readonly long _initialDropped;
    private readonly Func<double> _clock;
    private readonly double _startQpcMs;
    private double? _stopQpcMs;
    private DateTimeOffset? _requestedStop;
    private bool _drainConfirmed;
    private long _capacityDropped;
    private long _dropped;
    private int _count;
    private bool _disposed;
    private bool _limitReached;
    internal string DirectoryPath { get; }
    internal DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;
    internal int ProcessId { get; }
    internal string ProcessName { get; }
    internal bool LimitReached => _limitReached;

    internal CaptureWriter(int pid, string name, long initialDropped, string? captureRoot = null, Func<double>? timestampClock = null)
    {
        _clock = timestampClock ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
        _startQpcMs = _clock();
        ProcessId = pid; ProcessName = name; _initialDropped = initialDropped;
        DirectoryPath = Path.Combine(captureRoot ?? Path.Combine(SettingsStore.DirectoryPath, "Captures"), DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(DirectoryPath);
        _frames = new StreamWriter(Path.Combine(DirectoryPath, "frames.csv"));
        _sensors = null!;
        try
        {
            _sensors = new StreamWriter(Path.Combine(DirectoryPath, "sensors.csv"));
            _frames.WriteLine("process_id,swap_chain,qpc_ms,present_interval_ms,gpu_busy_ms,displayed_ms,frame_type");
            _sensors.WriteLine("utc,cpu_percent,cpu_c,gpu_percent,gpu_c,cpu_w,gpu_w,gpu_limit_w,gpu_clock_mhz,gpu_fan_percent,gpu_slowdown_c,gpu_limit_reason,ram_used_gib,ram_total_gib,vram_used_gib,vram_total_gib,vram_reserved_gib,commit_used_gib,commit_limit_gib,process_id,process_cpu_percent,process_working_set_gib");
            File.WriteAllText(Path.Combine(DirectoryPath, "session.json"), JsonSerializer.Serialize(new
            {
                version = Program.Version,
                processId = pid,
                processName = name,
                startedUtc = Started,
                frameMetric = "Application present interval; not a claim of displayed or generated FPS",
                limits = "1,000,000 frames / 60 minutes",
                state = "recording"
            }, SettingsStore.JsonOptions));
        }
        catch { _frames.Dispose(); _sensors?.Dispose(); throw; }
    }

    internal void RequestStop()
    {
        lock (_gate)
        {
            _stopQpcMs ??= _clock();
            _requestedStop ??= DateTimeOffset.UtcNow;
        }
    }
    internal void ConfirmDrain(bool complete) { lock (_gate) _drainConfirmed = complete; }

    internal void Write(CollectorPacket packet)
    {
        lock (_gate)
        {
            if (_disposed || _limitReached) return;
            _dropped = Math.Max(_dropped, packet.DroppedFrames - _initialDropped);
            foreach (var frame in packet.Frames)
            {
                if (frame.ProcessId != ProcessId || !double.IsFinite(frame.TimestampMs) || frame.TimestampMs - frame.DurationMs < _startQpcMs
                    || (_stopQpcMs is double cutoff && frame.TimestampMs > cutoff) || !double.IsFinite(frame.DurationMs) || frame.DurationMs <= 0) continue;
                if (_count >= 1_000_000) { _limitReached = true; break; }
                if (!_durations.TryGetValue(frame.SwapChain, out var samples))
                {
                    if (_durations.Count >= 32) { _capacityDropped++; continue; }
                    _durations[frame.SwapChain] = samples = [];
                }
                samples.Add(frame.DurationMs); _count++;
                _frames.WriteLine(string.Join(',', frame.ProcessId.ToString(CultureInfo.InvariantCulture), Escape(frame.SwapChain), Number(frame.TimestampMs), Number(frame.DurationMs), Number(frame.GpuBusyMs), Number(frame.DisplayedMs), Escape(frame.FrameType)));
            }
            var s = packet.Sensors;
            if (s.Timestamp < Started || (_requestedStop is { } stopped && s.Timestamp > stopped)) return;
            if (s.ProcessId != ProcessId) s = s with { ProcessCpu = null, ProcessRamGb = null };
            _sensors.WriteLine(string.Join(',', s.Timestamp.ToString("O"), Number(s.CpuLoad), Number(s.CpuTemperature), Number(s.GpuLoad), Number(s.GpuTemperature), Number(s.CpuPower), Number(s.GpuPower), Number(s.GpuPowerLimit), Number(s.GpuClock), Number(s.GpuFan), Number(s.GpuThermalLimit), Escape(s.GpuLimitReason), Number(s.RamUsedGb), Number(s.RamTotalGb), Number(s.VramUsedGb), Number(s.VramTotalGb), Number(s.VramReservedGb), Number(s.CommitUsedGb), Number(s.CommitTotalGb), s.ProcessId.ToString(CultureInfo.InvariantCulture), Number(s.ProcessCpu), Number(s.ProcessRamGb)));
            _frames.Flush(); _sensors.Flush();
        }
    }

    private static string Number(double? d) => d?.ToString("G17", CultureInfo.InvariantCulture) ?? "";
    private static string Escape(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            RequestStop();
            _disposed = true;
            try { _frames.Dispose(); } finally { _sensors.Dispose(); }
            var primary = _durations.OrderByDescending(kv => kv.Value.Count).FirstOrDefault();
            var stats = primary.Value is null ? null : FrameStatistics.Calculate(primary.Value);
            File.WriteAllText(Path.Combine(DirectoryPath, "summary.json"), JsonSerializer.Serialize(new
            {
                version = Program.Version,
                processId = ProcessId,
                processName = ProcessName,
                startedUtc = Started,
                endedUtc = _requestedStop,
                finalizedUtc = DateTimeOffset.UtcNow,
                primarySwapChain = primary.Key,
                statistics = stats,
                capturedFrames = _count,
                droppedEventsOrSamples = _dropped + _capacityDropped,
                limitReached = _limitReached,
                finalDrainConfirmed = _drainConfirmed,
                complete = _drainConfirmed && _dropped == 0 && _capacityDropped == 0 && !_limitReached && stats is not null,
                percentileMethod = "nearest-rank P99",
                lowMethod = "1000 / mean slowest ceil(N*0.01) present intervals"
            }, SettingsStore.JsonOptions));
        }
    }
}
