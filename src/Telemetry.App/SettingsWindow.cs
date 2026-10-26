using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace Telemetry.App;

internal sealed class SettingsWindow : Window
{
    internal void ShowControls()
    {
        Show();
        Native.ShowWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, 9);
        Activate();
    }
    internal SettingsWindow(AppSettings settings, Action<AppSettings, int> apply, Action advanced,
        Func<string> diagnostics, Action toggle, Action edit, Action capture, int currentTarget)
    {
        Title = "Telemetry · Controls"; Width = 700; Height = 680; MinWidth = 620; MinHeight = 520;
        Background = ColorBrush("#181818"); Foreground = ColorBrush("#E8E8E8"); FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        SourceInitialized += (_, _) => { int dark = 1; Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        Resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
        <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#D4D4D4"/></Style>
        <Style TargetType="TextBox"><Setter Property="Background" Value="#222222"/><Setter Property="Foreground" Value="#F0F0F0"/><Setter Property="BorderBrush" Value="#454545"/><Setter Property="Padding" Value="8,6"/><Setter Property="CaretBrush" Value="#F0F0F0"/></Style>
        <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#D4D4D4"/><Setter Property="Margin" Value="0,7,0,7"/></Style>
        <Style TargetType="Button"><Setter Property="Background" Value="#282828"/><Setter Property="Foreground" Value="#F0F0F0"/><Setter Property="BorderBrush" Value="#484848"/><Setter Property="Padding" Value="13,8"/><Setter Property="Margin" Value="0,0,8,0"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="#3A3A3A"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="BorderBrush" Value="#87AFAF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="#F0F0F0"/><Setter Property="Padding" Value="8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Row" Padding="{TemplateBinding Padding}" Background="#222222"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#404040"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Row" Property="Background" Value="#343D3D"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#F0F0F0"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#222222" BorderBrush="#454545" BorderThickness="1" Padding="8"><TextBlock Text="⌄" Foreground="#D4D4D4" HorizontalAlignment="Right"/></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" Margin="9,7,26,7" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Placement="Bottom" Focusable="False"><Border Background="#222222" BorderBrush="#454545" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="250"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="TabControl"><Setter Property="Background" Value="#181818"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl"><DockPanel><TabPanel DockPanel.Dock="Top" IsItemsHost="True"/><Border BorderBrush="#343434" BorderThickness="0,1,0,0" Padding="0,16,0,0"><ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent"/></Border></DockPanel></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="TabItem"><Setter Property="Foreground" Value="#A3A3A3"/><Setter Property="Padding" Value="16,12"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem"><Border x:Name="Tab" Background="#181818" BorderThickness="0,0,0,2" BorderBrush="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter ContentSource="Header"/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#F0F0F0"/><Setter TargetName="Tab" Property="BorderBrush" Value="#87AFAF"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Tab" Property="Background" Value="#282828"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        <Style TargetType="ListBox"><Setter Property="Background" Value="#222222"/><Setter Property="Foreground" Value="#F0F0F0"/><Setter Property="BorderBrush" Value="#454545"/><Setter Property="Padding" Value="6"/></Style>
        <Style TargetType="ListBoxItem"><Setter Property="Padding" Value="8,5"/></Style>
        <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Background" Value="#1C1C1C"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="{TemplateBinding Background}"><Track x:Name="PART_Track" IsDirectionReversed="True" Orientation="Vertical"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#484848" Margin="2,0"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0"/></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """);
        var root = new DockPanel { Margin = new Thickness(28) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) }; DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "TELEMETRY / CONTROLS", FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = ColorBrush("#87AFAF") });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        AddButton(actions, "Show / hide", toggle); AddButton(actions, "Move / resize", edit); AddButton(actions, "Record / stop", capture); header.Children.Add(actions);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var save = new Button { Content = "Apply changes", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) }; DockPanel.SetDock(save, Dock.Right); footer.Children.Add(save);
        var feedback = new TextBlock { Text = "Changes apply immediately when saved.", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Foreground = ColorBrush("#A3A3A3") }; footer.Children.Add(feedback);
        var tabs = new TabControl(); root.Children.Add(tabs);
        StackPanel Page(string name)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
        }
        void Explain(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = ColorBrush("#A3A3A3"), Margin = new Thickness(0, 0, 0, 12) });
        TextBox Field(Panel panel, string label, string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Text = text }; System.Windows.Automation.AutomationProperties.SetName(box, label);
            Grid.SetColumn(box, 1); row.Children.Add(box); panel.Children.Add(row); return box;
        }
        CheckBox Toggle(Panel panel, string label, bool enabled) { var check = new CheckBox { Content = label, IsChecked = enabled }; panel.Children.Add(check); return check; }
        ComboBox Select(Panel panel, string label, string[] items, int selected = 0)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 7, 0, 5) });
            var box = new ComboBox { ItemsSource = items, SelectedIndex = selected, Margin = new Thickness(0, 0, 0, 12) };
            System.Windows.Automation.AutomationProperties.SetName(box, label); panel.Children.Add(box); return box;
        }
        var display = Page("Display");
        Explain(display, $"Ctrl+Alt+{settings.ToggleKey} toggles the panel. Ctrl+Alt+E unlocks it: drag anywhere to move, or drag an edge to resize. Escape locks it again.");
        var key = Field(display, "Toggle key · Ctrl + Alt +", settings.ToggleKey);
        var scale = Field(display, "Text / interface scale", settings.Scale.ToString(CultureInfo.InvariantCulture));
        var opacity = Field(display, "Panel opacity · 0.5–1", settings.Opacity.ToString(CultureInfo.InvariantCulture));
        var width = Field(display, "Panel width · logical pixels", settings.PanelWidth.ToString("0", CultureInfo.InvariantCulture));
        var height = Field(display, "Panel height · logical pixels", settings.PanelHeight.ToString("0", CultureInfo.InvariantCulture));
        string originalWidth = width.Text, originalHeight = height.Text;
        string[] modes = ["Foreground", "Pointer", "Pinned"];
        var mode = Select(display, "Display selection", ["Active application's display", "Display under the pointer", "Fixed display"], Array.IndexOf(modes, settings.MonitorMode));
        var monitors = Forms.Screen.AllScreens.Select(s => s.DeviceName).ToArray();
        var monitorNames = Forms.Screen.AllScreens.Select((s, i) => $"Display {i + 1} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? " · primary" : "")}").ToArray();
        var monitor = Select(display, "Fixed display", monitorNames, Math.Max(0, Array.IndexOf(monitors, settings.PinnedMonitor)));
        var startup = Toggle(display, "Launch at Windows sign-in", settings.StartAtSignIn);
        var show = Toggle(display, "Show panel on launch", settings.ShowOnLaunch);
        var metrics = Page("Metrics");
        Explain(metrics, "Heavy GPU/CPU utilization is teal. Amber and red indicate configured memory/temperature thresholds. A high workload alone is not a fault.");
        Explain(metrics, "Select individual rows below. Power is the GPU's board reading; application memory is its working set. Neither is a total-system power or private-memory measurement.");
        var fps = Field(metrics, "Frame-time budget · target FPS", settings.TargetFps.ToString());
        var gpuWarn = Field(metrics, "GPU warning / critical °C", $"{settings.GpuWarningC} / {settings.GpuCriticalC}");
        var accent = Field(metrics, "Active / busy color", settings.Theme.Accent);
        var warning = Field(metrics, "Warning color", settings.Theme.Warning);
        var critical = Field(metrics, "Critical color", settings.Theme.Critical);
        var background = Field(metrics, "Panel background", settings.Theme.Background);
        var font = Field(metrics, "Typeface", settings.Theme.FontFamily);
        metrics.Children.Add(new TextBlock { Text = "Visible rows · select a row to reorder it", Margin = new Thickness(0, 6, 0, 6) });
        var order = new ListBox { Height = 240 };
        foreach (var id in settings.MetricOrder.Concat(AppSettings.MetricNames.Keys.Except(settings.MetricOrder)))
        {
            var row = new CheckBox
            {
                Content = AppSettings.MetricNames[id],
                Tag = id,
                IsChecked = settings.MetricOrder.Contains(id) && (settings.ShowDetails || id is not ("POWER" or "CLOCK" or "FAN" or "PROCESS")),
                Margin = new Thickness(4, 3, 4, 3)
            };
            row.Click += (_, _) => order.SelectedItem = row;
            order.Items.Add(row);
        }
        metrics.Children.Add(order);
        var rowActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 12) };
        foreach (var (label, delta) in new[] { ("Move up", -1), ("Move down", 1) }) AddButton(rowActions, label, () => { int i = order.SelectedIndex, n = i + delta; if (i < 0 || n < 0 || n >= order.Items.Count) return; var item = order.Items[i]; order.Items.RemoveAt(i); order.Items.Insert(n, item); order.SelectedIndex = n; });
        metrics.Children.Add(rowActions);
        var capturePage = Page("Capture");
        Explain(capturePage, "Ctrl+Alt+R records the selected process. The target stays fixed during a recording. CSV files and a JSON summary are saved locally.");
        var targets = new List<(int Pid, string Label)> { (0, "Automatic / foreground application") };
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try { if (process.MainWindowHandle != 0 && process.Id != Environment.ProcessId) targets.Add((process.Id, $"{process.ProcessName} / {process.Id}")); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        var target = Select(capturePage, "Target application", targets.Select(t => t.Label).ToArray(), Math.Max(0, targets.FindIndex(t => t.Pid == currentTarget)));
        Explain(capturePage, "FPS measures successful DirectX application Present calls. It is not generated/displayed FPS, input latency, or a GPU-busy measurement. OpenGL/Vulkan coverage depends on the application's presentation path and is not guaranteed.");
        Explain(capturePage, "1% low = reciprocal of the mean slowest 1% of present intervals. P99 uses nearest-rank frame time. Live statistics use a 10-second window; each recording gets its own summary.");
        var captures = new StackPanel { Orientation = Orientation.Horizontal }; AddButton(captures, "Open captures", () => { var path = Path.Combine(SettingsStore.DirectoryPath, "Captures"); Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = true }); }); capturePage.Children.Add(captures);
        var system = Page("System");
        Explain(system, "Uses Windows and your installed graphics driver. No sensor driver, companion monitoring app, account, or internet connection is required.");
        var status = new TextBox { Text = diagnostics(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 180, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 16) }; system.Children.Add(status);
        var refresh = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        refresh.Tick += (_, _) => { if (tabs.SelectedIndex == 3) status.Text = diagnostics(); };
        refresh.Start(); Closed += (_, _) => refresh.Stop();
        var systemActions = new StackPanel { Orientation = Orientation.Horizontal };
        AddButton(systemActions, "Refresh status", () => status.Text = diagnostics()); AddButton(systemActions, "Enable FPS capture…", advanced); system.Children.Add(systemActions);
        Explain(system, "\nWindows requires an administrator-approved collector for cross-process FPS tracing. This is a Windows permission, not an extra installation. The panel itself stays unprivileged.");
        Explain(system, "CPU temperature is not exposed through a universal Windows API. This build intentionally does not install a kernel driver. Detailed GPU telemetry currently supports NVIDIA's installed NVML driver API.");
        save.Click += (_, _) =>
        {
            try
            {
                var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
                candidate.StartAtSignIn = startup.IsChecked == true; candidate.ShowOnLaunch = show.IsChecked == true; candidate.ShowDetails = true;
                candidate.ToggleKey = key.Text.Trim().ToUpperInvariant(); candidate.Scale = double.Parse(scale.Text, CultureInfo.InvariantCulture); candidate.Opacity = double.Parse(opacity.Text, CultureInfo.InvariantCulture);
                if (width.Text != originalWidth) candidate.PanelWidth = double.Parse(width.Text, CultureInfo.InvariantCulture);
                if (height.Text != originalHeight) candidate.PanelHeight = double.Parse(height.Text, CultureInfo.InvariantCulture);
                candidate.TargetFps = int.Parse(fps.Text);
                candidate.MonitorMode = modes[Math.Max(0, mode.SelectedIndex)]; candidate.PinnedMonitor = monitors[Math.Max(0, monitor.SelectedIndex)];
                var temperature = gpuWarn.Text.Split('/'); if (temperature.Length != 2) throw new FormatException("Temperature format: warning / critical.");
                candidate.GpuWarningC = double.Parse(temperature[0], CultureInfo.InvariantCulture); candidate.GpuCriticalC = double.Parse(temperature[1], CultureInfo.InvariantCulture);
                candidate.Theme.Accent = accent.Text.Trim(); candidate.Theme.Warning = warning.Text.Trim(); candidate.Theme.Critical = critical.Text.Trim(); candidate.Theme.Background = background.Text.Trim(); candidate.Theme.FontFamily = font.Text.Trim();
                candidate.MetricOrder = order.Items.Cast<CheckBox>().Where(row => row.IsChecked == true).Select(row => (string)row.Tag).ToArray();
                candidate.PanelHeight = Math.Max(candidate.PanelHeight, candidate.GetMinimumHeight()); candidate.Validate(); apply(candidate, targets[target.SelectedIndex].Pid);
                width.Text = originalWidth = candidate.PanelWidth.ToString("0", CultureInfo.InvariantCulture);
                height.Text = originalHeight = candidate.PanelHeight.ToString("0", CultureInfo.InvariantCulture);
                feedback.Foreground = ColorBrush("#87AFAF"); feedback.Text = "Saved. The panel is up to date.";
            }
            catch (Exception e) when (e is FormatException or OverflowException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.SecurityException)
            { feedback.Foreground = ColorBrush("#FF8787"); feedback.Text = e.Message; }
        };
    }
    private static Brush ColorBrush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static void AddButton(Panel panel, string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => action(); panel.Children.Add(button); }
}
