namespace Telemetry.Core;

public sealed record SensorSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string CpuName { get; init; } = "CPU";
    public string GpuName { get; init; } = "GPU";
    public double? CpuLoad { get; init; }
    public double? CpuTemperature { get; init; }
    public double? CpuPower { get; init; }
    public double? CpuClock { get; init; }
    public double? GpuLoad { get; init; }
    public double? GpuTemperature { get; init; }
    public double? GpuPower { get; init; }
    public double? GpuClock { get; init; }
    public double? GpuFan { get; init; }
    public double? GpuPowerLimit { get; init; }
    public double? GpuThermalLimit { get; init; }
    public string? GpuLimitReason { get; init; }
    public double? CommitUsedGb { get; init; }
    public double? CommitTotalGb { get; init; }
    public double? ProcessCpu { get; init; }
    public int ProcessId { get; init; }
    public double? ProcessRamGb { get; init; }
    public double? VramUsedGb { get; init; }
    public double? VramTotalGb { get; init; }
    public double? VramReservedGb { get; init; }
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
    public string SensorStatus { get; init; } = "Starting sensors";
}

public sealed record FrameSample(int ProcessId, string SwapChain, double TimestampMs,
    double DurationMs, double? GpuBusyMs, double? DisplayedMs, string? FrameType);

public sealed record CollectorCommand(int ProcessId, string ProcessName, bool CollectFrames = true, long FinishCaptureId = 0);
public sealed record CollectorPacket(SensorSnapshot Sensors, FrameSample[] Frames,
    string FrameStatus, long DroppedFrames, bool Elevated, long FinishedCaptureId = 0, bool CaptureDrainComplete = false);

public enum Severity { Normal, Busy, Warning, Critical, Unavailable }

public sealed record ThemeSettings
{
    public string Background { get; set; } = "#181818";
    public string Foreground { get; set; } = "#F0F0F0";
    public string Muted { get; set; } = "#A3A3A3";
    public string Accent { get; set; } = "#87AFAF";
    public string Warning { get; set; } = "#D7AF87";
    public string Critical { get; set; } = "#FF8787";
    public string FontFamily { get; set; } = "Hack Nerd Font Mono, Cascadia Mono, Consolas";
}

public sealed record Placement(double X, double Y);
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool StartAtSignIn { get; set; }
    public bool ShowOnLaunch { get; set; } = true;
    public bool FollowForeground { get; set; } = true;
    public string MonitorMode { get; set; } = "Foreground";
    public string? PinnedMonitor { get; set; }
    public string FollowCorner { get; set; } = "TopRight";
    public bool IncludeBackgroundProcesses { get; set; }
    public double Scale { get; set; } = 1;
    public double Opacity { get; set; } = .96;
    public double PanelWidth { get; set; } = 360;
    public double PanelHeight { get; set; } = 554;
    public int TargetFps { get; set; } = 144;
    public string ToggleKey { get; set; } = "O";
    public bool ShowDetails { get; set; } = true;
    public double CpuWarningC { get; set; } = 85;
    public double CpuCriticalC { get; set; } = 95;
    public double GpuWarningC { get; set; } = 80;
    public double GpuCriticalC { get; set; } = 90;
    public string[] MetricOrder { get; set; } = ["GPU", "CPU", "VRAM", "RAM", "POWER", "CLOCK", "FAN", "PROCESS"];
    public static IReadOnlyDictionary<string, string> MetricNames { get; } = new Dictionary<string, string>
    {
        ["GPU"] = "GPU utilization / temperature", ["CPU"] = "CPU utilization · all cores", ["VRAM"] = "Video memory · used / total",
        ["RAM"] = "System memory · used / total", ["POWER"] = "GPU power · current / limit", ["CLOCK"] = "Graphics clock",
        ["FAN"] = "GPU fan speed", ["PROCESS"] = "Application CPU / working set", ["COMMIT"] = "Committed memory · used / limit"
    };
    public Dictionary<string, Placement> Placements { get; set; } = [];
    public ThemeSettings Theme { get; set; } = new();
    public double GetMinimumHeight() => Math.Max(420, 325 + MetricOrder.Count(id => ShowDetails || id is not ("POWER" or "CLOCK" or "FAN" or "PROCESS")) * 18);

    public void Validate()
    {
        if (Theme is null || MetricOrder is null || Placements is null || ToggleKey is null)
            throw new InvalidDataException("Settings must include theme, metrics, placements and a toggle key.");
        if (SchemaVersion != 1) throw new InvalidDataException("Unsupported settings version.");
        if (!double.IsFinite(Scale) || Scale < .75 || Scale > 2) throw new InvalidDataException("Scale must be 0.75–2.");
        if (!double.IsFinite(Opacity) || Opacity < .5 || Opacity > 1) throw new InvalidDataException("Opacity must be 0.5–1.");
        if (TargetFps is < 24 or > 1000) throw new InvalidDataException("Target FPS must be 24–1000.");
        if (ToggleKey.Length != 1 || ToggleKey[0] is < 'A' or > 'Z' || ToggleKey is "E" or "R" or "S") throw new InvalidDataException("Hotkey must be A–Z, excluding E, R and S (reserved shortcuts).");
        if (MonitorMode is not ("Foreground" or "Pointer" or "Pinned")) throw new InvalidDataException("Invalid monitor mode.");
        if (FollowCorner is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight")) throw new InvalidDataException("Invalid follow corner.");
        foreach (var pair in new[] { (CpuWarningC, CpuCriticalC), (GpuWarningC, GpuCriticalC) })
            if (!double.IsFinite(pair.Item1) || !double.IsFinite(pair.Item2) || pair.Item1 < 30 || pair.Item2 > 120 || pair.Item1 >= pair.Item2)
                throw new InvalidDataException("Temperature limits must be ordered and within 30–120°C.");
        if (!double.IsFinite(PanelWidth) || PanelWidth < 300 || PanelWidth > 900 || !double.IsFinite(PanelHeight) || PanelHeight < 420 || PanelHeight > 1400)
            throw new InvalidDataException("Panel dimensions are outside the supported range.");
        var known = MetricNames.Keys.ToArray();
        if (MetricOrder.Length > known.Length || MetricOrder.Distinct().Count() != MetricOrder.Length || MetricOrder.Any(m => !known.Contains(m)))
            throw new InvalidDataException("Invalid or duplicate metric rows.");
        if (PanelHeight < GetMinimumHeight()) throw new InvalidDataException($"These rows need a panel height of at least {GetMinimumHeight():0} logical pixels.");
        if (Placements.Count > 32 || Placements.Values.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new InvalidDataException("Invalid monitor placements.");
        foreach (var color in new[] { Theme.Background, Theme.Foreground, Theme.Muted, Theme.Accent, Theme.Warning, Theme.Critical })
            if (color is null || color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
                throw new InvalidDataException("Theme colors must be #RRGGBB.");
        if (string.IsNullOrWhiteSpace(Theme.FontFamily) || Theme.FontFamily.Length > 256) throw new InvalidDataException("Enter a typeface name (up to 256 characters).");
        if (Theme.FontFamily.Any(c => char.IsControl(c) || c is ':' or '/' or '\\' or '#')) throw new InvalidDataException("Use installed font-family names only; font URLs and paths are not supported.");
    }
}
