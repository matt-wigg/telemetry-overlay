using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Telemetry.App;

internal sealed class OverlayWindow : Window
{
    private readonly HudCanvas _canvas;
    private readonly AppSettings _settings;
    private nint _handle;
    private readonly StackPanel _editTools;
    private bool _applyingSize;
    private System.Drawing.Rectangle _lastWorkingArea;
    internal bool Editing { get; private set; }
    internal bool NativeVisible => _handle != 0 && Native.IsWindowVisible(_handle);
    internal event Action? Moved;
    internal event Action? EditRequested;
    internal string CurrentMonitor { get; private set; } = "";

    internal OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        Title = "Telemetry overlay";
        Width = settings.PanelWidth * settings.Scale; Height = settings.PanelHeight * settings.Scale;
        MinWidth = 300 * settings.Scale; MinHeight = settings.GetMinimumHeight() * settings.Scale;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        var root = new Grid();
        _canvas = new HudCanvas(settings); root.Children.Add(_canvas);
        _editTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, 35, 10, 0), Visibility = Visibility.Collapsed };
        foreach (var label in new[] { "SETTINGS", "LOCK" })
        {
            var button = new Button { Content = label, FontSize = 10, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(4, 0, 0, 0), Background = new SolidColorBrush(Color.FromRgb(34, 34, 34)), Foreground = Brushes.WhiteSmoke, BorderBrush = Brushes.DimGray };
            button.Click += (_, _) => { if (label == "LOCK") SetEditing(false); else EditRequested?.Invoke(); };
            _editTools.Children.Add(button);
        }
        root.Children.Add(_editTools); Content = root;
        Opacity = settings.Opacity;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_handle)?.AddHook(ResizeHook);
            ApplyInputMode();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (!Editing) return;
            if (e.ClickCount == 2) { EditRequested?.Invoke(); return; }
            try { DragMove(); SavePosition(); Moved?.Invoke(); }
            catch (InvalidOperationException) { }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetEditing(false); };
        SizeChanged += (_, _) =>
        {
            if (!Editing || _applyingSize) return;
            _settings.PanelWidth = Math.Clamp(ActualWidth / _settings.Scale, 300, 900);
            _settings.PanelHeight = Math.Clamp(ActualHeight / _settings.Scale, 420, 1400);
        };
    }

    internal void SetEditing(bool enabled)
    {
        Editing = enabled;
        _editTools.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyInputMode();
        _canvas.Editing = enabled;
        _canvas.InvalidateVisual();
        if (enabled) { Activate(); Keyboard.Focus(this); }
        if (!enabled) { SavePosition(); Moved?.Invoke(); }
    }
    internal void ShowPanel()
    {
        Show();
        if (_handle != 0) Native.ShowWindow(_handle, 4);
        Place(true);
    }
    private nint ResizeHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0232 && Editing) { SavePosition(); Moved?.Invoke(); }
        if (message != 0x0084 || !Editing) return 0;
        Native.GetWindowRect(hWnd, out var r);
        int x = (short)(lParam.ToInt64() & 0xffff), y = (short)((lParam.ToInt64() >> 16) & 0xffff);
        int edge = (int)(9 * VisualTreeHelper.GetDpi(this).DpiScaleX);
        bool left = x < r.Left + edge, right = x >= r.Right - edge, top = y < r.Top + edge, bottom = y >= r.Bottom - edge;
        int hit = top ? left ? 13 : right ? 14 : 12 : bottom ? left ? 16 : right ? 17 : 15 : left ? 10 : right ? 11 : 0;
        if (hit != 0) { handled = true; return hit; }
        return 0;
    }
    private void ApplyInputMode()
    {
        if (_handle == 0) return;
        var style = Native.GetWindowLongPtr(_handle, Native.GwlExStyle).ToInt64() | Native.WsExToolWindow;
        style = Editing ? style & ~(Native.WsExTransparent | Native.WsExNoActivate) : style | Native.WsExTransparent | Native.WsExNoActivate;
        Native.SetWindowLongPtr(_handle, Native.GwlExStyle, (nint)style);
    }
    internal void RefreshTheme()
    {
        _applyingSize = true;
        MinWidth = 300 * _settings.Scale; MinHeight = _settings.GetMinimumHeight() * _settings.Scale;
        MaxWidth = 900 * _settings.Scale; MaxHeight = 1400 * _settings.Scale;
        Width = _settings.PanelWidth * _settings.Scale;
        Height = _settings.PanelHeight * _settings.Scale;
        _applyingSize = false;
        Opacity = _settings.Opacity;
        _canvas.RefreshTheme();
        Place(true);
    }

    internal void Place(bool force = false)
    {
        if (_handle == 0 || Editing) return;
        var foreground = Native.Foreground();
        Forms.Screen screen;
        if (_settings.MonitorMode == "Pinned") screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.PinnedMonitor) ?? Forms.Screen.PrimaryScreen!;
        else if (_settings.MonitorMode == "Pointer") { Native.GetCursorPos(out var p); screen = Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)); }
        else screen = foreground.Pid == Environment.ProcessId ? Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == CurrentMonitor) ?? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(foreground.Window);
        if (!force && screen.DeviceName == CurrentMonitor && screen.WorkingArea == _lastWorkingArea) return;
        CurrentMonitor = screen.DeviceName;
        var bounds = screen.WorkingArea;
        _lastWorkingArea = bounds;
        Native.GetWindowRect(_handle, out var rect);
        int width = Math.Max(100, rect.Right - rect.Left), height = Math.Max(100, rect.Bottom - rect.Top);
        var position = _settings.Placements.GetValueOrDefault(screen.DeviceName, new Placement(bounds.Width - width - 24, 24));
        int x = bounds.Left + (int)Math.Clamp(position.X, 0, Math.Max(0, bounds.Width - width));
        int y = bounds.Top + (int)Math.Clamp(position.Y, 0, Math.Max(0, bounds.Height - height));
        Native.SetWindowPos(_handle, new nint(-1), x, y, 0, 0, 0x0010 | 0x0001);
        _canvas.MonitorLabel = "DISPLAY " + (Array.FindIndex(Forms.Screen.AllScreens, s => s.DeviceName == CurrentMonitor) + 1).ToString("00");
    }

    private void SavePosition()
    {
        Native.GetWindowRect(_handle, out var rect);
        var screen = Forms.Screen.FromHandle(_handle);
        CurrentMonitor = screen.DeviceName;
        var b = screen.WorkingArea;
        int x = rect.Left - b.Left, y = rect.Top - b.Top;
        int maxX = Math.Max(0, b.Width - (rect.Right - rect.Left)), maxY = Math.Max(0, b.Height - (rect.Bottom - rect.Top));
        if (Math.Abs(x) < 24) x = 16; else if (Math.Abs(x - maxX) < 24) x = maxX - 16;
        if (Math.Abs(y) < 24) y = 16; else if (Math.Abs(y - maxY) < 24) y = maxY - 16;
        _settings.Placements[screen.DeviceName] = new(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
        Native.SetWindowPos(_handle, new nint(-1), b.Left + Math.Clamp(x, 0, maxX), b.Top + Math.Clamp(y, 0, maxY), 0, 0, 0x0010 | 0x0001);
    }
    internal void Update(CollectorPacket? packet, FrameStatistics? statistics, double[] graph, string target, string status, bool recording, bool stale)
    {
        _canvas.Packet = packet; _canvas.Statistics = statistics; _canvas.Graph = graph;
        _canvas.Target = target; _canvas.Status = status; _canvas.Recording = recording; _canvas.Stale = stale;
        _canvas.UpdateSeverities();
        if (IsVisible) _canvas.InvalidateVisual();
    }
}

internal sealed class HudCanvas : FrameworkElement
{
    private readonly AppSettings _settings;
    private Typeface _font = new("Consolas");
    private Brush _background = Brushes.Black, _foreground = Brushes.White, _muted = Brushes.Gray, _accent = Brushes.Teal, _warning = Brushes.Orange, _critical = Brushes.Red;
    private readonly Dictionary<string, SeverityLatch> _latches = [];
    private readonly Dictionary<string, Severity> _severities = [];
    internal CollectorPacket? Packet { get; set; }
    internal FrameStatistics? Statistics { get; set; }
    internal double[] Graph { get; set; } = [];
    internal string Target { get; set; } = "Desktop";
    internal string Status { get; set; } = "Starting collector";
    internal string MonitorLabel { get; set; } = "DISPLAY 01";
    internal bool Editing { get; set; }
    internal bool Recording { get; set; }
    internal bool Stale { get; set; }
    internal HudCanvas(AppSettings settings) { _settings = settings; RefreshTheme(); }

    internal void RefreshTheme()
    {
        var t = _settings.Theme;
        static Brush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        _background = Brush(t.Background); _foreground = Brush(t.Foreground); _muted = Brush(t.Muted);
        _accent = Brush(t.Accent); _warning = Brush(t.Warning); _critical = Brush(t.Critical);
        _font = new Typeface(new FontFamily(t.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        InvalidateVisual();
    }
    internal void UpdateSeverities()
    {
        var s = Stale ? null : Packet?.Sensors;
        void Update(string id, double? value, double warning, double critical, bool utilization = false)
        {
            if (!_latches.TryGetValue(id, out var latch)) _latches[id] = latch = new();
            _severities[id] = latch.Update(value, warning, critical, utilization);
        }
        Update("GPU", s?.GpuLoad, 90, 100, true); Update("CPU", s?.CpuLoad, 90, 100, true);
        Update("GPUtemp", s?.GpuTemperature, _settings.GpuWarningC, _settings.GpuCriticalC);
        Update("CPUtemp", s?.CpuTemperature, _settings.CpuWarningC, _settings.CpuCriticalC);
        Update("VRAM", s?.VramTotalGb > 0 ? s.VramUsedGb / s.VramTotalGb * 100 : null, 90, 98);
        Update("RAM", s?.RamTotalGb > 0 ? s.RamUsedGb / s.RamTotalGb * 100 : null, 90, 98);
        Update("COMMIT", s?.CommitTotalGb > 0 ? s.CommitUsedGb / s.CommitTotalGb * 100 : null, 90, 98);
    }
    private Brush Tone(string key) => _severities.GetValueOrDefault(key) switch { Severity.Busy => _accent, Severity.Warning => _warning, Severity.Critical => _critical, Severity.Unavailable => _muted, _ => _foreground };

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(_settings.Scale, _settings.Scale));
        double height = ActualHeight / _settings.Scale, width = ActualWidth / _settings.Scale;
        double rightEdge = width - 18;
        dc.DrawRectangle(_background, new Pen(Editing ? _accent : new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Rect(.5, .5, width - 1, height - 1));
        void Text(string text, double x, double y, double size, Brush? brush = null, double maxWidth = 280, bool right = false)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _font, size, brush ?? _foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(formatted, new Point(right ? x - formatted.Width : x, y));
        }
        static string Number(double? value, string format = "0") => value is double v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "—";
        Text("TELEMETRY", 18, 14, 11, _muted);
        Text(Recording ? "[REC]" : MonitorLabel, rightEdge, 14, 10, Recording ? _critical : _accent, right: true);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(0, 33), new Point(width, 33));
        Text(Target, 18, 43, 12, _foreground, Editing ? width - 180 : width - 36);
        var stats = Stale ? null : Statistics;
        Text(Number(stats?.Fps, "0.0"), 16, 72, 38);
        Text("Application FPS", rightEdge, 80, 11, _muted, right: true);
        Text("10 s rolling window", rightEdge, 99, 10, _muted, right: true);
        Text("1% low  " + Number(stats?.OnePercentLow, "0.0"), 18, 125, 11, _muted);
        Text("Avg " + Number(stats?.MeanMs, "0.00") + " ms", rightEdge, 125, 11, _muted, right: true);
        double graphTop = 149, graphBottom = 191;
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(18, graphBottom), new Point(rightEdge, graphBottom));
        if (Graph.Length > 1 && !Stale)
        {
            double ceiling = Math.Max(1000.0 / _settings.TargetFps * 2, Graph.Where(double.IsFinite).DefaultIfEmpty(1).Max() * 1.15);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                bool connected = false;
                for (int i = 0; i < Graph.Length; i++)
                {
                    if (!double.IsFinite(Graph[i])) { connected = false; continue; }
                    var p = new Point(18 + (60 - Graph.Length + i) * (width - 36) / 59, graphBottom - Math.Clamp(Graph[i] / ceiling, 0, 1) * (graphBottom - graphTop));
                    if (!connected) context.BeginFigure(p, false, false); else context.LineTo(p, true, false);
                    connected = true;
                }
            }
            geometry.Freeze(); dc.DrawGeometry(null, new Pen(_accent, 1.3), geometry);
            double budgetY = graphBottom - (1000.0 / _settings.TargetFps / ceiling) * (graphBottom - graphTop);
            dc.DrawLine(new Pen(_muted, .4), new Point(18, budgetY), new Point(rightEdge, budgetY));
        }
        Text("30 s / 0.5 s peaks", 18, 198, 10, _muted);
        if (stats is null)
        {
            string help = Stale ? "Waiting for fresh measurements" : Status.StartsWith("FPS permission required", StringComparison.Ordinal)
                ? "Enable FPS: Controls > System" : "Waiting for this app to present frames";
            Text(help, 18, 158, 11, _muted, width - 36);
        }
        Text((1000.0 / _settings.TargetFps).ToString("0.00", CultureInfo.InvariantCulture) + " ms budget", rightEdge, 198, 10, _muted, right: true);
        Text("P99 frame " + Number(stats?.P99Ms, "0.00") + " ms", 18, 219, 11, _muted);
        Text((stats?.Count.ToString(CultureInfo.InvariantCulture) ?? "0") + " samples", rightEdge, 218, 10, _muted, right: true);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(18, 243), new Point(rightEdge, 243));
        var s = Stale ? null : Packet?.Sensors;
        double y = 254;
        int rowCount = _settings.MetricOrder.Count(id => _settings.ShowDetails || id is not ("POWER" or "CLOCK" or "FAN" or "PROCESS"));
        double rowHeight = Math.Clamp((height - 325) / Math.Max(1, rowCount), 18, 33);
        foreach (var id in _settings.MetricOrder)
        {
            if (!_settings.ShowDetails && id is "POWER" or "CLOCK" or "FAN" or "PROCESS") continue;
            var (primary, secondary) = id switch
            {
                "GPU" => (Number(s?.GpuLoad) + "%", Number(s?.GpuTemperature) + "°C"),
                "CPU" => (Number(s?.CpuLoad) + "%", "all cores"),
                "VRAM" => (Number(s?.VramUsedGb, "0.0"), "/ " + Number(s?.VramTotalGb, "0.#") + " GiB"),
                "RAM" => (Number(s?.RamUsedGb, "0.0"), "/ " + Number(s?.RamTotalGb, "0.#") + " GiB"),
                "COMMIT" => (Number(s?.CommitUsedGb, "0.0"), "/ " + Number(s?.CommitTotalGb, "0.#") + " GiB"),
                "POWER" => (Number(s?.GpuPower) + " W", "/ " + Number(s?.GpuPowerLimit) + " W"),
                "CLOCK" => (Number(s?.GpuClock) + " MHz", "GPU"),
                "FAN" => (Number(s?.GpuFan) + "%", "speed"),
                "PROCESS" => (Number(s?.ProcessCpu, "0.0") + "%", Number(s?.ProcessRamGb, "0.0") + " GiB"),
                _ => ("—", "")
            };
            var label = id switch { "GPU" => "GPU utilization", "CPU" => "CPU utilization", "VRAM" => "Video memory", "RAM" => "System memory", "POWER" => "GPU power", "CLOCK" => "Graphics clock", "FAN" => "GPU fan", "PROCESS" => "App CPU / RAM", "COMMIT" => "Committed memory", _ => id };
            if (width < 340) label = id switch
            {
                "GPU" => "GPU usage", "CPU" => "CPU usage", "VRAM" => "Video RAM", "RAM" => "System RAM",
                "CLOCK" => "GPU clock", "PROCESS" => "App CPU/RAM", "COMMIT" => "Commit", _ => label
            };
            Text(label, 18, y, 11, _muted, width - 218);
            Text(primary, rightEdge - 88, y - 1, 13, Tone(id), 90, true);
            Text(secondary, rightEdge, y, 11, id == "GPU" ? Tone(id + "temp") : _muted, 82, true);
            if (_severities.GetValueOrDefault(id) is Severity.Warning or Severity.Critical || (id == "GPU" && _severities.GetValueOrDefault("GPUtemp") is Severity.Warning or Severity.Critical)) Text("!", 7, y, 11, _warning);
            y += rowHeight;
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(0, height - 61), new Point(width, height - 61));
        Text(s?.GpuLimitReason is { } limit ? "GPU limit: " + limit : s?.GpuName ?? "Graphics device unavailable", 18, height - 52, 10, s?.GpuLimitReason is null ? _muted : _warning, width - 36);
        Text(Editing ? "Drag to move / drag edges to resize" : Stale ? "Data stale / collector unavailable" : Status, 18, height - 34, 10, Stale ? _warning : _muted, width - 36);
        Text(Editing ? "Escape locks the panel" : "Ctrl+Alt+" + _settings.ToggleKey + " hides / Ctrl+Alt+E edits", 18, height - 18, 10, Editing ? _accent : _muted);
        if (Editing) { dc.DrawLine(new Pen(_accent, 1), new Point(width - 14, height - 5), new Point(width - 5, height - 14)); dc.DrawLine(new Pen(_accent, 1), new Point(width - 9, height - 5), new Point(width - 5, height - 9)); }
        dc.Pop();
    }
}
