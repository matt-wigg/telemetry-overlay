using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Telemetry.App;

internal sealed class SettingsWindow : Window
{
    internal void SelectPageForVerification(int index)
    {
        if (Content is DockPanel root) root.Children.OfType<TabControl>().Single().SelectedIndex = index;
    }
    internal void ShowControls()
    {
        Show();
        Native.ShowWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, 9);
        Topmost = true;
        Activate();
    }

    internal SettingsWindow(AppSettings settings, Action<AppSettings, int> apply, Action advanced,
        Func<string> diagnostics, Action toggle, Action capture, Func<int> currentTarget)
    {
        Title = "Telemetry · Controls"; Width = 760; Height = 740; MinWidth = 700; MinHeight = 620;
        Background = ColorBrush("#181818"); Foreground = ColorBrush("#E8E8E8");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        Activated += (_, _) => Topmost = true;
        Deactivated += (_, _) => Topmost = false;
        Resources = new ResourceDictionary { Source = new Uri("/Telemetry;component/Themes/Controls.xaml", UriKind.Relative) };
        SourceInitialized += (_, _) =>
        {
            int dark = 1;
            Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
            FitToDisplay();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(FitToDisplay);
        var root = new DockPanel { Margin = new Thickness(28) }; Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var identity = new StackPanel();
        identity.Children.Add(new TextBlock { Text = "Telemetry", FontSize = 24, Foreground = ColorBrush("#F0F0F0") });
        identity.Children.Add(new TextBlock { Text = "WIDGET CONTROLS", FontSize = 10, Foreground = ColorBrush("#87AFAF"), Margin = new Thickness(0, 5, 0, 0) });
        header.Children.Add(identity);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        AddButton(actions, "Show / hide", toggle);
        header.Children.Add(actions);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = true };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var save = new Button { Content = "Apply changes", Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var feedback = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = ColorBrush("#A3A3A3") };
        footer.Children.Add(feedback);
        var tabs = new TabControl(); root.Children.Add(tabs);

        StackPanel Page(string name)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            return panel;
        }
        void Section(Panel panel, string name) => panel.Children.Add(new TextBlock
        { Text = name.ToUpperInvariant(), FontSize = 10, Foreground = ColorBrush("#87AFAF"), Margin = new Thickness(0, 14, 0, 14) });
        TextBox Field(Panel panel, string label, string text, string? hint = null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint });
            var box = new TextBox { Text = text, ToolTip = hint };
            System.Windows.Automation.AutomationProperties.SetName(box, label);
            Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row); return box;
        }
        void Readout(Panel panel, string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var key = new TextBlock { Text = value, FontFamily = new FontFamily("Consolas"), Foreground = ColorBrush("#A3A3A3") };
            DockPanel.SetDock(key, Dock.Right); row.Children.Add(key);
            row.Children.Add(new TextBlock { Text = label }); panel.Children.Add(row);
        }
        CheckBox Toggle(Panel panel, string label, bool enabled)
        {
            var check = new CheckBox { Content = label, IsChecked = enabled };
            panel.Children.Add(check); return check;
        }
        ComboBox Select(Panel panel, string label, string[] items, int selected = 0)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 7) });
            var box = new ComboBox { ItemsSource = items, SelectedIndex = selected, Margin = new Thickness(0, 0, 0, 14) };
            System.Windows.Automation.AutomationProperties.SetName(box, label); panel.Children.Add(box); return box;
        }
        void Details(Panel panel, string title, string text)
        {
            panel.Children.Add(new Expander { Header = title, Foreground = ColorBrush("#A3A3A3"), Margin = new Thickness(0, 12, 0, 8),
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 10, 8, 4), Foreground = ColorBrush("#A3A3A3") } });
        }
        static string N(double number) => number.ToString("0.##", CultureInfo.InvariantCulture);
        static double Parse(TextBox box) => double.Parse(box.Text, CultureInfo.InvariantCulture);

        var general = Page("General");
        Section(general, "Display");
        string[] modes = ["Foreground", "Pinned"];
        var mode = Select(general, "Widget mode", ["Follow active app · snap to display corner", "Free placement · choose an app in the header"], Math.Max(0, Array.IndexOf(modes, settings.MonitorMode)));
        string[] corners = ["TopRight", "TopLeft", "BottomRight", "BottomLeft"];
        var corner = Select(general, "Follow corner", ["Top right", "Top left", "Bottom right", "Bottom left"], Array.IndexOf(corners, settings.FollowCorner));
        var screens = Forms.Screen.AllScreens;
        var monitors = screens.Select(s => s.DeviceName).ToArray();
        var monitor = Select(general, "Display", screens.Select((s, i) => $"Display {i + 1}{(s.Primary ? " · primary" : "")}").ToArray(), Math.Max(0, Array.IndexOf(monitors, settings.PinnedMonitor)));
        monitor.IsEnabled = mode.SelectedIndex == 1; corner.IsEnabled = mode.SelectedIndex == 0;
        mode.SelectionChanged += (_, _) => { monitor.IsEnabled = mode.SelectedIndex == 1; corner.IsEnabled = mode.SelectedIndex == 0; };
        var backgroundProcesses = Toggle(general, "Include background processes in the app picker", settings.IncludeBackgroundProcesses);
        EventHandler displayChanged = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            string? selected = monitor.SelectedIndex >= 0 && monitor.SelectedIndex < monitors.Length ? monitors[monitor.SelectedIndex] : settings.PinnedMonitor;
            screens = Forms.Screen.AllScreens;
            monitors = screens.Select(s => s.DeviceName).ToArray();
            monitor.ItemsSource = screens.Select((s, i) => $"Display {i + 1}{(s.Primary ? " · primary" : "")}").ToArray();
            monitor.SelectedIndex = Math.Max(0, Array.IndexOf(monitors, selected));
            FitToDisplay();
        });
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
        Section(general, "Startup");
        var startup = Toggle(general, "Launch at Windows sign-in", settings.StartAtSignIn);
        var show = Toggle(general, "Show widget on launch", settings.ShowOnLaunch);
        Section(general, "Shortcuts");
        var key = Field(general, "Show / hide · Ctrl + Alt +", settings.ToggleKey, "Choose A–Z. R and S are reserved.");
        Readout(general, "Open Controls", "Ctrl + Alt + S");
        Readout(general, "Record / stop", "Ctrl + Alt + R");
        Readout(general, "Lock after positioning", "Escape");
        Details(general, "Positioning", "Follow mode tracks the active application's display. Free placement stays on its chosen display while you use other apps; its header selects the process to monitor. Drag the top-right MOVE handle anytime, or resize an edge. Dragging switches to free placement. Moving across displays updates the fixed display. Each display keeps its own free position.");

        var appearance = Page("Appearance");
        Section(appearance, "Size");
        var scale = Field(appearance, "Text scale · %", N(settings.Scale * 100), "75–200%. Windows display scaling is applied separately.");
        var opacity = Field(appearance, "Opacity · %", N(settings.Opacity * 100), "50–100%.");
        var width = Field(appearance, "Width · logical px", N(settings.PanelWidth));
        var height = Field(appearance, "Height · logical px", N(settings.PanelHeight));
        string originalWidth = width.Text, originalHeight = height.Text;
        Section(appearance, "Theme");
        var background = Field(appearance, "Background", settings.Theme.Background);
        var accent = Field(appearance, "Active / busy", settings.Theme.Accent);
        var warning = Field(appearance, "Warning", settings.Theme.Warning);
        var critical = Field(appearance, "Critical", settings.Theme.Critical);
        var font = Field(appearance, "Typeface", settings.Theme.FontFamily, "Installed font-family names; comma-separated fallbacks are supported.");
        Details(appearance, "Color meanings", "High CPU/GPU utilization is a busy workload, shown in the active color. Warning and critical colors mark memory pressure or configured temperature thresholds.");

        var metrics = Page("Metrics");
        Section(metrics, "Visible rows");
        var order = new ListBox { Height = 260 };
        foreach (var id in settings.MetricOrder.Concat(AppSettings.MetricNames.Keys.Except(settings.MetricOrder)))
        {
            var row = new CheckBox { Content = AppSettings.MetricNames[id], Tag = id,
                IsChecked = settings.MetricOrder.Contains(id) && (settings.ShowDetails || id is not ("POWER" or "CLOCK" or "FAN" or "PROCESS")),
                Margin = new Thickness(4, 3, 4, 3) };
            row.Click += (_, _) => order.SelectedItem = row;
            order.Items.Add(row);
        }
        System.Windows.Automation.AutomationProperties.SetName(order, "Visible metric rows");
        metrics.Children.Add(order);
        var rowActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 4) };
        foreach (var (label, delta) in new[] { ("Move up", -1), ("Move down", 1) })
            AddButton(rowActions, label, () =>
            {
                int i = order.SelectedIndex, next = i + delta;
                if (i < 0 || next < 0 || next >= order.Items.Count) return;
                var item = order.Items[i]; order.Items.RemoveAt(i); order.Items.Insert(next, item); order.SelectedIndex = next;
            });
        metrics.Children.Add(rowActions);
        Section(metrics, "Thresholds");
        var fps = Field(metrics, "Graph reference · FPS", settings.TargetFps.ToString(CultureInfo.InvariantCulture), "Sets the minimum frame-time graph range. Does not cap game FPS.");
        var gpuWarn = Field(metrics, "GPU warning · °C", N(settings.GpuWarningC));
        var gpuCritical = Field(metrics, "GPU critical · °C", N(settings.GpuCriticalC));
        Details(metrics, "Units and sources", "GPU power is board power. App memory is working set. Memory uses GiB. The graph shows the peak present interval in each 0.5-second bucket. CPU temperature is unavailable without a supported driver; this app installs none.");

        var capturePage = Page("Capture");
        Section(capturePage, "Application");
        Readout(capturePage, "Target", "Selected in the widget header");
        var captureActions = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton(captureActions, "Record / stop", capture);
        AddButton(captureActions, "Open captures", () =>
        {
            string? error = FolderLauncher.OpenCaptures();
            feedback.Text = error ?? "Opened captures in File Explorer";
            feedback.Foreground = ColorBrush(error is null ? "#87AFAF" : "#FF8787");
        });
        capturePage.Children.Add(captureActions);
        Section(capturePage, "Recording");
        Readout(capturePage, "Shortcut", "Ctrl + Alt + R");
        Readout(capturePage, "Output", "CSV + JSON");
        Readout(capturePage, "Limits", "60 min / 1 million intervals");
        Details(capturePage, "Measurement definitions", "FPS is the application's successful Present-call rate, averaged over 10 seconds. Frame time is the mean interval in milliseconds. Low FPS is the 1% low: 1000 divided by the mean slowest 1% of intervals. The background graph shows peak frame times in half-second buckets over 30 seconds; taller spikes mean slower frames. These are not displayed/generated FPS or input latency. P99 and exact statistics remain in capture exports. An em dash means unavailable, not zero.");
        Details(capturePage, "Capture completeness", "The process stays fixed while recording. Stopping drains buffered frames. Check summary.json for completeness, loss and sample count. Raw captures contain process identifiers and measurements; inspect them before sharing.");

        var system = Page("Diagnostics");
        Section(system, "Collector");
        var systemActions = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton(systemActions, "Enable FPS…", advanced);
        system.Children.Add(systemActions);
        var status = new TextBox { Text = diagnostics(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            MinHeight = 260, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 16, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(status, "Live collector diagnostics");
        system.Children.Add(status);
        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refresh.Tick += (_, _) => { if (tabs.SelectedIndex == 4) status.Text = diagnostics(); };
        refresh.Start(); Closed += (_, _) => refresh.Stop();
        Details(system, "Permissions and compatibility", "One Windows approval enables FPS for all applications in this collector session. Switching games needs no further approval. Exiting Telemetry or restarting the collector ends the session. The widget stays unprivileged. GPU sensors use the installed NVIDIA driver; unsupported values remain unavailable. Borderless/windowed games are the intended overlay modes.");

        save.Click += (_, _) =>
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
                candidate.StartAtSignIn = startup.IsChecked == true; candidate.ShowOnLaunch = show.IsChecked == true; candidate.ShowDetails = true;
                candidate.ToggleKey = key.Text.Trim().ToUpperInvariant(); candidate.Scale = Parse(scale) / 100; candidate.Opacity = Parse(opacity) / 100;
                if (width.Text != originalWidth) candidate.PanelWidth = Parse(width);
                if (height.Text != originalHeight) candidate.PanelHeight = Parse(height);
                candidate.TargetFps = int.Parse(fps.Text, CultureInfo.InvariantCulture);
                candidate.MonitorMode = modes[Math.Max(0, mode.SelectedIndex)];
                candidate.FollowCorner = corners[Math.Max(0, corner.SelectedIndex)];
                candidate.IncludeBackgroundProcesses = backgroundProcesses.IsChecked == true;
                candidate.PinnedMonitor = monitors.Length == 0 ? null : monitors[Math.Clamp(monitor.SelectedIndex, 0, monitors.Length - 1)];
                candidate.GpuWarningC = Parse(gpuWarn); candidate.GpuCriticalC = Parse(gpuCritical);
                candidate.Theme.Accent = accent.Text.Trim(); candidate.Theme.Warning = warning.Text.Trim();
                candidate.Theme.Critical = critical.Text.Trim(); candidate.Theme.Background = background.Text.Trim(); candidate.Theme.FontFamily = font.Text.Trim();
                candidate.MetricOrder = order.Items.Cast<CheckBox>().Where(row => row.IsChecked == true).Select(row => (string)row.Tag).ToArray();
                candidate.PanelHeight = Math.Max(candidate.PanelHeight, candidate.GetMinimumHeight());
                candidate.Validate(); apply(candidate, candidate.MonitorMode == "Foreground" ? 0 : currentTarget());
                width.Text = originalWidth = N(candidate.PanelWidth); height.Text = originalHeight = N(candidate.PanelHeight);
                feedback.Foreground = ColorBrush("#87AFAF"); feedback.Text = "Changes saved";
            }
            catch (Exception e) when (e is FormatException or OverflowException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.SecurityException)
            { feedback.Foreground = ColorBrush("#FF8787"); feedback.Text = e.Message; }
        };
    }
    private void FitToDisplay()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        var area = Forms.Screen.FromHandle(handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = Math.Max(1, area.Width - 16) / dpi.DpiScaleX;
        double height = Math.Max(1, area.Height - 16) / dpi.DpiScaleY;
        MinWidth = Math.Min(700, width); MinHeight = Math.Min(620, height);
        Width = Math.Min(Width, width); Height = Math.Min(Height, height);
        Native.GetWindowRect(handle, out var bounds);
        int x = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - (bounds.Right - bounds.Left)));
        int y = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - (bounds.Bottom - bounds.Top)));
        Native.SetWindowPos(handle, 0, x, y, 0, 0, 0x0015);
    }

    private static Brush ColorBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static void AddButton(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label }; button.Click += (_, _) => action(); panel.Children.Add(button);
    }
}
