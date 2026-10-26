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
    private readonly ComboBox _targetPicker;
    private bool _refreshingTargets;
    private ProcessChoice? _selectedTarget;
    private string _automaticLabel = "Active application";
    internal event Action<ProcessChoice?>? TargetSelected;
    private nint _handle;
    private bool _applyingSize;
    private bool _dragging;
    private Native.Point _dragStart;
    private Native.Rect _dragBounds;
    private System.Drawing.Rectangle _lastWorkingArea;
    internal bool Editing { get; private set; }
    internal double RenderScale => _canvas.RenderScale;
    internal bool NativeVisible => _handle != 0 && Native.IsWindowVisible(_handle);
    internal event Action? Moved;
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
        Resources = new ResourceDictionary { Source = new Uri("/Telemetry;component/Themes/Widget.xaml", UriKind.Relative) };
        _canvas = new HudCanvas(settings);
        var root = new Grid(); root.Children.Add(_canvas); Content = root;
        _targetPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily(settings.Theme.FontFamily), FontSize = 11,
            Style = (Style)Resources["TargetPicker"], ToolTip = "Choose the application to monitor. System rows remain system-wide.",
            IsTextSearchEnabled = true
        };
        TextSearch.SetTextPath(_targetPicker, "Name");
        System.Windows.Automation.AutomationProperties.SetName(_targetPicker, "Monitored application");
        root.Children.Add(_targetPicker);
        _targetPicker.DropDownOpened += (_, _) => RefreshTargets();
        _targetPicker.SelectionChanged += (_, _) =>
        {
            if (_refreshingTargets || _targetPicker.SelectedItem is not TargetItem item) return;
            TargetSelected?.Invoke(item.Choice);
        };
        Opacity = settings.Opacity;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_handle)?.AddHook(ResizeHook);
            ApplyInputMode();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SetEditing(false); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!Editing) Place(true); });
        SizeChanged += (_, _) =>
        {
            LayoutPicker();
            if (!Editing || _applyingSize) return;
            _settings.PanelWidth = Math.Clamp(ActualWidth / _canvas.RenderScale, 300, 900);
            _settings.PanelHeight = Math.Clamp(ActualHeight / _canvas.RenderScale, _settings.GetMinimumHeight(), 1400);
        };
        RefreshTargets();
    }

    private sealed record TargetItem(string Name, ProcessChoice? Choice)
    {
        public string Detail => Choice is null ? "AUTO" : "PID " + Choice.Pid.ToString(CultureInfo.InvariantCulture);
        public override string ToString() => Name;
    }
    private void RefreshTargets()
    {
        _refreshingTargets = true;
        try
        {
            var choices = ProcessChoice.List(_settings.IncludeBackgroundProcesses).ToList();
            if (_selectedTarget is not null && !choices.Contains(_selectedTarget)) choices.Insert(0, _selectedTarget);
            var items = new List<TargetItem> { new(_automaticLabel, null) };
            items.AddRange(choices.Select(p => new TargetItem(p.Name, p)));
            _targetPicker.ItemsSource = items;
            _targetPicker.SelectedItem = items.FirstOrDefault(i => i.Choice == _selectedTarget) ?? items[0];
        }
        finally { _refreshingTargets = false; }
    }
    internal void SetTarget(ProcessChoice? choice, bool recording)
    {
        if (_selectedTarget != choice) { _selectedTarget = choice; RefreshTargets(); }
        _targetPicker.IsEnabled = !recording;
    }
    internal bool VerifyPickerCaption(string expected)
    {
        _targetPicker.ApplyTemplate(); _targetPicker.UpdateLayout();
        bool HasCaption(DependencyObject parent)
        {
            if (parent is TextBlock text && text.Text == expected)
                return text.TextTrimming == TextTrimming.CharacterEllipsis && text.ActualWidth <= _targetPicker.ActualWidth;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (HasCaption(VisualTreeHelper.GetChild(parent, i))) return true;
            return false;
        }
        return HasCaption(_targetPicker);
    }
    internal bool VerifyPickerInputRouting()
    {
        SetEditing(true);
        _targetPicker.IsDropDownOpen = true;
        try
        {
            bool pressHandled = false, hitHandled = false;
            ResizeHook(_handle, 0x0201, 0, (nint)((200 << 16) | 100), ref pressHandled);
            Native.GetWindowRect(_handle, out var bounds);
            long corner = ((long)(ushort)bounds.Top << 16) | (ushort)bounds.Left;
            ResizeHook(_handle, 0x0084, 0, (nint)corner, ref hitHandled);
            return !pressHandled && !hitHandled && !_dragging;
        }
        finally { _targetPicker.IsDropDownOpen = false; SetEditing(false); }
    }
    private void LayoutPicker(double? previewWidth = null)
    {
        double scale = RenderScale;
        _targetPicker.Visibility = _settings.MonitorMode == "Pinned" ? Visibility.Visible : Visibility.Collapsed;
        _targetPicker.Margin = new Thickness(8 * scale, 1 * scale, 0, 0);
        _targetPicker.Width = Math.Max(150, (previewWidth ?? ActualWidth) / scale - 110);
        _targetPicker.Height = 31;
        _targetPicker.LayoutTransform = new ScaleTransform(scale, scale);
    }
    // The same content can be laid out for regression images without opening a desktop window.
    internal FrameworkElement PreparePreview(double width, double height)
    {
        LayoutPicker(width);
        var surface = (FrameworkElement)Content;
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        surface.UpdateLayout();
        return surface;
    }

    internal void SetEditing(bool enabled)
    {
        if (!enabled) FinishDrag();
        Editing = enabled;
        ApplyInputMode();
        _canvas.Editing = enabled;
        _canvas.InvalidateVisual();
        if (enabled) { Activate(); Keyboard.Focus(this); }
        if (!enabled) { SavePosition(); Place(true); Moved?.Invoke(); }
    }
    private void FinishDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (Native.GetCapture() == _handle) Native.ReleaseCapture();
        SavePosition(); Moved?.Invoke();
    }
    private void MoveToCursor()
    {
        if (Native.GetCursorPos(out var point))
            Native.SetWindowPos(_handle, new nint(-1), _dragBounds.Left + point.X - _dragStart.X,
                _dragBounds.Top + point.Y - _dragStart.Y, 0, 0, 0x0011);
    }
    internal void ShowPanel()
    {
        Show();
        if (_handle != 0) Native.ShowWindow(_handle, 4);
        Place(true);
    }
    private nint ResizeHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Use the press message's coordinates, not a later live cursor position: queued
        // input can already have moved by the time the UI thread handles the press.
        // Avoid Windows' move loop, which Aero Snaps widgets at display boundaries.
        // WPF captures mouse input to the owner while its ComboBox popup is open.
        // Leave those messages to WPF so clicking a list item cannot start a drag.
        if (message == 0x0201 && Editing && !_targetPicker.IsDropDownOpen)
        {
            // The free-placement header is an interactive selector, not a drag handle.
            double clientY = (short)((lParam.ToInt64() >> 16) & 0xffff) / VisualTreeHelper.GetDpi(this).DpiScaleY;
            if (_settings.MonitorMode == "Pinned" && clientY < 36 * RenderScale) return 0;
            _dragStart = new Native.Point { X = (short)(lParam.ToInt64() & 0xffff), Y = (short)((lParam.ToInt64() >> 16) & 0xffff) };
            if (Native.ClientToScreen(hWnd, ref _dragStart) && Native.GetWindowRect(hWnd, out _dragBounds))
            {
                Native.SetCapture(hWnd);
                _dragging = Native.GetCapture() == hWnd;
                handled = _dragging;
            }
            return 0;
        }
        if (_dragging && message is 0x0200 or 0x0202)
        {
            MoveToCursor();
            if (message == 0x0202) FinishDrag();
            handled = true; return 0;
        }
        if (_dragging && message is 0x0215 or 0x001F) FinishDrag();
        if (message == 0x0232 && Editing) { SavePosition(); Moved?.Invoke(); }
        if (message != 0x0084 || !Editing || _targetPicker.IsDropDownOpen) return 0;
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
        var style = Native.GetWindowLongPtr(_handle, Native.GwlExStyle).ToInt64();
        // Expose a normal, keyboard-accessible window while positioning; locked widgets stay out of Alt+Tab.
        style = Editing
            ? (style | Native.WsExAppWindow) & ~(Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow)
            : _settings.MonitorMode == "Pinned"
                ? (style | Native.WsExToolWindow) & ~(Native.WsExTransparent | Native.WsExNoActivate | Native.WsExAppWindow)
                : (style | Native.WsExTransparent | Native.WsExNoActivate | Native.WsExToolWindow) & ~Native.WsExAppWindow;
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
        _targetPicker.FontFamily = new FontFamily(_settings.Theme.FontFamily);
        _targetPicker.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.Theme.Background));
        _targetPicker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.Theme.Foreground));
        _targetPicker.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.Theme.Accent));
        ApplyInputMode(); LayoutPicker();
        Place(true);
    }

    internal void Place(bool force = false)
    {
        if (_handle == 0) return;
        var foreground = Native.Foreground();
        // A borderless game may reorder its own topmost window without changing monitors.
        // Keep our active Controls window above the widget, without taking focus from either.
        nint insertAfter = foreground.Pid == Environment.ProcessId && foreground.Window != _handle
            ? foreground.Window : new nint(-1);
        if (IsVisible) Native.SetWindowPos(_handle, insertAfter, 0, 0, 0, 0, 0x0013);
        if (Editing) return;
        Forms.Screen screen;
        if (_settings.MonitorMode == "Pinned") screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.PinnedMonitor) ?? Forms.Screen.PrimaryScreen!;
        else if (_settings.MonitorMode == "Pointer") { Native.GetCursorPos(out var p); screen = Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)); }
        else screen = foreground.Pid == Environment.ProcessId ? Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == CurrentMonitor) ?? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(foreground.Window);
        if (!force && screen.DeviceName == CurrentMonitor && screen.WorkingArea == _lastWorkingArea) return;
        CurrentMonitor = screen.DeviceName;
        var bounds = screen.WorkingArea;
        _lastWorkingArea = bounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        var layout = PanelLayout.Fit(_settings, Math.Max(1, bounds.Width - 16) / dpi.DpiScaleX, Math.Max(1, bounds.Height - 16) / dpi.DpiScaleY);
        _applyingSize = true;
        try
        {
            _canvas.RenderScale = layout.Scale;
            MinWidth = 300 * layout.Scale;
            MinHeight = _settings.GetMinimumHeight() * layout.Scale;
            Width = layout.Width; Height = layout.Height;
        }
        finally { _applyingSize = false; }
        Native.GetWindowRect(_handle, out var rect);
        int width = Math.Max(100, rect.Right - rect.Left), height = Math.Max(100, rect.Bottom - rect.Top);
        var position = _settings.MonitorMode == "Foreground"
            ? new Placement(_settings.FollowCorner.EndsWith("Right", StringComparison.Ordinal) ? bounds.Width - width - 24 : 24,
                _settings.FollowCorner.StartsWith("Bottom", StringComparison.Ordinal) ? bounds.Height - height - 24 : 24)
            : _settings.Placements.GetValueOrDefault(screen.DeviceName, new Placement(bounds.Width - width - 24, 24));
        int x = bounds.Left + (int)Math.Clamp(position.X, 0, Math.Max(0, bounds.Width - width));
        int y = bounds.Top + (int)Math.Clamp(position.Y, 0, Math.Max(0, bounds.Height - height));
        Native.SetWindowPos(_handle, insertAfter, x, y, 0, 0, 0x0010 | 0x0001);
        _canvas.MonitorLabel = "DISPLAY " + (Array.FindIndex(Forms.Screen.AllScreens, s => s.DeviceName == CurrentMonitor) + 1).ToString("00");
    }

    private void SavePosition()
    {
        Native.GetWindowRect(_handle, out var rect);
        var screen = Forms.Screen.FromHandle(_handle);
        CurrentMonitor = screen.DeviceName;
        if (_settings.MonitorMode == "Pinned") _settings.PinnedMonitor = CurrentMonitor;
        _canvas.MonitorLabel = "DISPLAY " + (Array.FindIndex(Forms.Screen.AllScreens, s => s.DeviceName == CurrentMonitor) + 1).ToString("00");
        var b = screen.WorkingArea;
        int x = rect.Left - b.Left, y = rect.Top - b.Top;
        int maxX = Math.Max(0, b.Width - (rect.Right - rect.Left)), maxY = Math.Max(0, b.Height - (rect.Bottom - rect.Top));
        if (Math.Abs(x) < 24) x = 16; else if (Math.Abs(x - maxX) < 24) x = maxX - 16;
        if (Math.Abs(y) < 24) y = 16; else if (Math.Abs(y - maxY) < 24) y = maxY - 16;
        _settings.Placements[screen.DeviceName] = new(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
        Native.SetWindowPos(_handle, new nint(-1), b.Left + Math.Clamp(x, 0, maxX), b.Top + Math.Clamp(y, 0, maxY), 0, 0, 0x0010 | 0x0001);
    }
    internal void Update(CollectorPacket? packet, FrameWindow window, string target, string status, bool recording, bool stale)
    {
        _canvas.Packet = packet; _canvas.Statistics = window.Statistics; _canvas.Graph = window.Graph;
        _canvas.FpsNote = stale ? "Collector delayed" : window.Statistics is not null ? "10 s average"
            : status.StartsWith("FPS permission required", StringComparison.Ordinal) ? "Enable FPS"
            : status.StartsWith("Application exited", StringComparison.Ordinal) ? "App exited"
            : window.LatestFrameAgeMs >= FrameWindow.FreshnessLimitMs ? "No recent frames"
            : window.LatestFrameAgeMs is not null ? "Collecting frames" : "Waiting for frames";
        _canvas.Target = target; _canvas.Status = status; _canvas.Recording = recording; _canvas.Stale = stale;
        if (_selectedTarget is null && !_targetPicker.IsDropDownOpen && _automaticLabel != target + " · auto")
        {
            _automaticLabel = target + " · auto";
            // Refresh just the selected caption; enumerate processes only when the picker opens.
            _refreshingTargets = true;
            _targetPicker.ItemsSource = new[] { new TargetItem(_automaticLabel, null) };
            _targetPicker.SelectedIndex = 0;
            _refreshingTargets = false;
        }
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
    internal string FpsNote { get; set; } = "Waiting for frames";
    internal string MonitorLabel { get; set; } = "DISPLAY 01";
    internal bool Editing { get; set; }
    internal bool Recording { get; set; }
    internal bool Stale { get; set; }
    internal double RenderScale { get; set; }
    internal HudCanvas(AppSettings settings) { _settings = settings; RenderScale = settings.Scale; RefreshTheme(); }

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

    private void DrawFrameHistory(DrawingContext dc, Rect bounds)
    {
        if (Stale || Graph.Length < 2) return;
        double ceiling = Math.Max(1000.0 / _settings.TargetFps * 2, Graph.Where(double.IsFinite).DefaultIfEmpty(1).Max() * 1.15);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var stroke = line.Open())
        using (var fill = area.Open())
        {
            bool connected = false;
            double lastX = bounds.Left;
            for (int i = 0; i < Graph.Length; i++)
            {
                if (!double.IsFinite(Graph[i]))
                {
                    if (connected) fill.LineTo(new Point(lastX, bounds.Bottom), true, false);
                    connected = false; continue;
                }
                double x = bounds.Left + (60 - Graph.Length + i) * bounds.Width / 59;
                var point = new Point(x, bounds.Bottom - Math.Clamp(Graph[i] / ceiling, 0, 1) * bounds.Height);
                if (!connected)
                {
                    stroke.BeginFigure(point, false, false);
                    fill.BeginFigure(new Point(x, bounds.Bottom), true, true);
                }
                else stroke.LineTo(point, true, false);
                fill.LineTo(point, true, false);
                connected = true; lastX = x;
            }
            if (connected) fill.LineTo(new Point(lastX, bounds.Bottom), true, false);
        }
        line.Freeze(); area.Freeze();
        dc.PushClip(new RectangleGeometry(bounds));
        dc.PushOpacity(.055); dc.DrawGeometry(_accent, null, area); dc.Pop();
        dc.PushOpacity(.28); dc.DrawGeometry(null, new Pen(_accent, 1.2), line); dc.Pop();
        dc.Pop();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(RenderScale, RenderScale));
        double height = ActualHeight / RenderScale, width = ActualWidth / RenderScale;
        double rightEdge = width - 18;
        dc.DrawRectangle(_background, new Pen(Editing ? _accent : new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Rect(.5, .5, width - 1, height - 1));
        void Text(string text, double x, double y, double size, Brush? brush = null, double maxWidth = 280, bool right = false)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _font, size, brush ?? _foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(formatted, new Point(right ? x - formatted.Width : x, y));
        }
        static string Number(double? value, string format = "0") => value is double v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "—";
        if (_settings.MonitorMode != "Pinned") Text(Target, 18, 13, 12, _foreground, width - 120);
        Text(Recording ? "[REC]" : MonitorLabel, rightEdge, 11, 10, Recording ? _critical : _accent, right: true);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(0, 33), new Point(width, 33));
        var stats = Stale ? null : Statistics;
        DrawFrameHistory(dc, new Rect(1, 34, width - 2, 204));
        Text("FPS", 18, 49, 11, _muted);
        if (stats is null) Text(FpsNote, rightEdge, 49, 10, _muted, 132, right: true);
        Text(Number(stats?.Fps), 16, 66, 52, _foreground, width - 34);
        double columnWidth = (width - 52) / 2;
        double secondColumn = 34 + columnWidth;
        Text("Frame time", 18, 159, 11, _muted, columnWidth);
        Text("Low FPS", secondColumn, 159, 11, _muted, columnWidth);
        Text(stats is null ? "—" : Number(stats.MeanMs, "0.00") + " ms", 18, 179, 18, _foreground, columnWidth);
        Text(Number(stats?.OnePercentLow), secondColumn, 179, 18, _foreground, columnWidth);
        Text("Frame-time history", 18, 219, 9, _muted, width - 110);
        Text("Last 30 s", rightEdge, 219, 9, _muted, 65, right: true);
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(18, 239), new Point(rightEdge, 239));
        var s = Stale ? null : Packet?.Sensors;
        double y = 251;
        int rowCount = _settings.MetricOrder.Count(id => _settings.ShowDetails || id is not ("POWER" or "CLOCK" or "FAN" or "PROCESS"));
        double rowHeight = Math.Clamp((height - 290) / Math.Max(1, rowCount), 18, 33);
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
                "CLOCK" => (Number(s?.GpuClock) + " MHz", ""),
                "FAN" => (Number(s?.GpuFan) + "%", ""),
                "PROCESS" => (Number(s?.ProcessCpu, "0.0") + "%", Number(s?.ProcessRamGb, "0.0") + " GiB"),
                _ => ("—", "")
            };
            var label = id switch
            {
                "GPU" => "GPU usage", "CPU" => "CPU usage", "VRAM" => "Video RAM", "RAM" => "System RAM",
                "POWER" => "GPU power", "FAN" => "GPU fan", "CLOCK" => "GPU clock", "PROCESS" => "App CPU/RAM", "COMMIT" => "Committed", _ => id
            };
            Text(label, 18, y, 11, _muted, width - 218);
            Text(primary, rightEdge - 88, y - 1, 13, Tone(id), 90, true);
            Text(secondary, rightEdge, y, 11, id == "GPU" ? Tone(id + "temp") : _muted, 82, true);
            if (_severities.GetValueOrDefault(id) is Severity.Warning or Severity.Critical || (id == "GPU" && _severities.GetValueOrDefault("GPUtemp") is Severity.Warning or Severity.Critical)) Text("!", 7, y, 11, _warning);
            y += rowHeight;
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(52, 52, 52)), 1), new Point(0, height - 33), new Point(width, height - 33));
        string state = Stale ? "STALE DATA" : Packet?.DroppedFrames > 0 ? "EVENT LOSS" : Recording ? "RECORDING" : Editing ? "MOVE / RESIZE" : s?.GpuLimitReason ?? (stats is not null ? "PRESENT / LIVE" : Status.StartsWith("FPS permission required", StringComparison.Ordinal) ? "FPS LOCKED" : "NO PRESENTS");
        Text(state, 18, height - 21, 9, Stale || Packet?.DroppedFrames > 0 || s?.GpuLimitReason is not null ? _warning : Editing ? _accent : _muted, width - 174);
        Text("Ctrl+Alt", rightEdge - 127, height - 21, 9, _muted, 48);
        string[] keys = [_settings.ToggleKey, "E", "S"];
        for (int i = 0; i < keys.Length; i++)
        {
            double x = rightEdge - 72 + i * 26;
            dc.DrawRectangle(null, new Pen(_muted, .6), new Rect(x, height - 26, 20, 19));
            Text(keys[i], x + 7, height - 22, 10, _foreground, 12);
        }
        if (Editing) dc.DrawLine(new Pen(_accent, 1), new Point(width - 7, height - 3), new Point(width - 3, height - 7));
        dc.Pop();
    }
}
