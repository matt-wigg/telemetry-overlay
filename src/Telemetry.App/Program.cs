using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Telemetry.App;

internal static class Program
{
    internal static string Version => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--quit"])
            {
                try { using var quit = EventWaitHandle.OpenExisting(@"Local\TelemetryQuit-7B9F1ED2"); quit.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args is ["--frame-workload", var reference]) return NativeValidation.RunWorkload(reference);
            if (args is ["--verify-fps", var report])
            {
                using var diagnosticMutex = new Mutex(true, @"Local\TelemetryOverlay-7B9F1ED2", out bool exclusive);
                if (!exclusive) return 2;
                return NativeValidation.VerifyAsync(report).GetAwaiter().GetResult();
            }
            if (args is ["--collector", var pipe]) { CollectorHost.RunAsync(pipe).GetAwaiter().GetResult(); return 0; }
            if (args is ["--probe"])
            {
                using var sensor = new SensorProvider(); Thread.Sleep(1000);
                Console.WriteLine(JsonSerializer.Serialize(sensor.Read())); return 0;
            }
            // Small instrumentation windows must remain visible in remote/captured desktops,
            // including systems where the WPF hardware surface is not composed correctly.
            System.Windows.Media.RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TelemetryControls-7B9F1ED2");
            using var mutex = new Mutex(true, @"Local\TelemetryOverlay-7B9F1ED2", out bool first);
            if (!first) { if (!args.Contains("--startup")) activation.Set(); return 0; }
            using var quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\TelemetryQuit-7B9F1ED2");
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.DispatcherUnhandledException += (_, e) => { SettingsStore.Log(e.Exception); };
            using var controller = new AppController(application, args.Contains("--smoke-test"));
            var activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
                application.Dispatcher.BeginInvoke(controller.OpenSettings), null, Timeout.Infinite, false);
            var quitWait = ThreadPool.RegisterWaitForSingleObject(quitEvent, (_, _) =>
                application.Dispatcher.BeginInvoke(application.Shutdown), null, Timeout.Infinite, false);
            application.Startup += async (_, _) =>
            {
                await controller.StartAsync();
                if (args.Contains("--fps")) await controller.EnableFpsAsync();
                if (args.Contains("--settings")) controller.OpenSettings();
                if (args is ["--smoke-test", var output]) await controller.SmokeTestAsync(output);
            };
            try { application.Run(); }
            finally { activationWait.Unregister(null); quitWait.Unregister(null); }
            return 0;
        }
        catch (Exception e) { SettingsStore.Log(e); return 1; }
    }
}

internal sealed class AppController : IDisposable
{
    private readonly Application _application;
    private readonly AppSettings _settings;
    private readonly bool _testing;
    private readonly CollectorClient _collector = new();
    private readonly object _dataGate = new();
    private readonly RingBuffer<FrameSample> _history = new(32768);
    private readonly Forms.NotifyIcon _tray;
    private readonly Icon _icon;
    private readonly OverlayWindow _overlay;
    private readonly DispatcherTimer _timer;
    private readonly HwndSource _hotkeyWindow;
    private CollectorPacket? _packet;
    private FrameWindow? _frameWindow;
    private CaptureWriter? _capture;
    private SettingsWindow? _settingsWindow;
    private long _lastPacket;
    private int _targetPid, _manualPid, _candidatePid, _candidateTicks;
    private ProcessChoice? _manualTarget;
    private ProcessChoice? _captureTarget;
    private string _targetName = "Desktop", _status = "Starting collector";
    private bool _restarting;
    private bool _disposed;
    private bool _ticking;
    private long _captureSequence, _captureFinishId, _captureStopStarted;
    private readonly List<Task> _captureSaves = [];
    private readonly uint _taskbarCreated;
    private int _toggleHotkeyId = 1;
    private string? _registeredToggle;
    private bool _editHotkeyRegistered, _captureHotkeyRegistered, _settingsHotkeyRegistered;

    internal AppController(Application application, bool testing = false)
    {
        _testing = testing;
        _settings = testing ? new() : SettingsStore.Load();
        _application = application;
        _overlay = new(_settings);
        _overlay.Moved += SaveSettings;
        _overlay.TargetSelected += choice =>
        {
            if (_capture is not null) { Notify("Stop the recording before changing applications."); return; }
            SelectTarget(choice);
        };
        _hotkeyWindow = new HwndSource(new HwndSourceParameters("Telemetry hotkeys") { WindowStyle = 0, ExtendedWindowStyle = 0x80, Width = 0, Height = 0 });
        _hotkeyWindow.AddHook(WindowHook);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        _icon = CreateIcon();
        _tray = new Forms.NotifyIcon { Text = "Telemetry · Ctrl+Alt+O", Icon = _icon, Visible = true, ContextMenuStrip = BuildMenu() };
        _tray.DoubleClick += (_, _) => _application.Dispatcher.Invoke(ToggleOverlay);
        _collector.Packet += Receive;
        _collector.Status += s => _application.Dispatcher.BeginInvoke(() => { _status = s; });
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, async (_, _) => await TickAsync(), _application.Dispatcher);
    }

    internal async Task StartAsync()
    {
        RegisterHotkeys(_settings.ToggleKey);
        _overlay.ShowPanel(); _overlay.RefreshTheme();
        if (!_settings.ShowOnLaunch) _overlay.Hide();
        await RestartCollectorAsync(false);
        if (SettingsStore.LoadWarning is { } warning) Notify(warning);
    }

    internal void OpenSettings() => ShowSettings();
    internal Task EnableFpsAsync()
    {
        if (_packet?.Elevated == true && Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPacket)).TotalSeconds <= 3)
        {
            Notify("FPS access is already enabled for all applications in this session.");
            return Task.CompletedTask;
        }
        return RestartCollectorAsync(true);
    }

    internal async Task SmokeTestAsync(string output)
    {
        Directory.CreateDirectory(output);
        await Task.Delay(6000);
        _overlay.ShowPanel(); _overlay.RefreshTheme();
        await Task.Delay(500);
        ToggleOverlay(); bool hideWorks = !_overlay.NativeVisible;
        ToggleOverlay(); bool showWorks = _overlay.NativeVisible;
        _overlay.SetEditing(true);
        var editStyle = Native.GetWindowLongPtr(new WindowInteropHelper(_overlay).Handle, Native.GwlExStyle).ToInt64();
        bool editReceivesInput = (editStyle & (Native.WsExTransparent | Native.WsExNoActivate)) == 0;
        _overlay.Width = 400 * _overlay.RenderScale; _overlay.Height = 620 * _overlay.RenderScale;
        await Task.Delay(100);
        bool resizeWorks = Math.Abs(_settings.PanelWidth - 400) < 1 && Math.Abs(_settings.PanelHeight - 620) < 1;
        _overlay.SetEditing(false);
        bool positionRemembered = _settings.Placements.ContainsKey(_overlay.CurrentMonitor);
        var displayChecks = new List<object>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            _settings.MonitorMode = "Pinned"; _settings.PinnedMonitor = screen.DeviceName;
            _overlay.RefreshTheme();
            await Task.Delay(250);
            var overlayHandle = new WindowInteropHelper(_overlay).Handle;
            Native.GetWindowRect(overlayHandle, out var before);
            _overlay.SetEditing(true);
            Native.SetWindowPos(overlayHandle, new nint(-1), screen.WorkingArea.Left + 60, screen.WorkingArea.Top + 80, 0, 0, 0x0011);
            _overlay.Width = 380 * _overlay.RenderScale; _overlay.Height = 600 * _overlay.RenderScale;
            await Task.Delay(100);
            _overlay.SetEditing(false);
            Native.GetWindowRect(overlayHandle, out var saved);
            _overlay.Hide(); _overlay.ShowPanel();
            await Task.Delay(100);
            Native.GetWindowRect(overlayHandle, out var restored);
            var area = screen.WorkingArea;
            displayChecks.Add(new
            {
                monitor = screen.DeviceName,
                placedOnDisplay = before.Left >= area.Left && before.Top >= area.Top && before.Right <= area.Right && before.Bottom <= area.Bottom,
                resizePersisted = Math.Abs(_settings.PanelWidth - 380) < 1 && Math.Abs(_settings.PanelHeight - 600) < 1,
                positionRestored = saved.Left == restored.Left && saved.Top == restored.Top,
                visibleAfterToggle = _overlay.NativeVisible,
                displayRemembered = _overlay.CurrentMonitor == screen.DeviceName
            });
        }
        _settings.MonitorMode = "Foreground";
        _settings.PanelWidth = 360; _settings.PanelHeight = 554; _overlay.RefreshTheme();
        await Task.Delay(100);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)_overlay.ActualWidth, (int)_overlay.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(_overlay);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "overlay.png"))) encoder.Save(file);
        ShowSettings();
        await Task.Delay(200);
        var handle = new WindowInteropHelper(_overlay).Handle;
        Native.GetWindowRect(handle, out var bounds);
        var styles = Native.GetWindowLongPtr(handle, Native.GwlExStyle).ToInt64();
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(output, "smoke.json"), JsonSerializer.Serialize(new
        {
            sensors = _packet?.Sensors,
            collectorStatus = _status,
            trayVisible = _tray.Visible,
            overlayVisible = _overlay.NativeVisible,
            controlsVisible = Native.IsWindowVisible(new WindowInteropHelper(_settingsWindow!).Handle),
            hideWorks,
            showWorks,
            editReceivesInput,
            resizeWorks,
            positionRemembered,
            toggleHotkeyRegistered = _registeredToggle is not null,
            editHotkeyRegistered = _editHotkeyRegistered,
            captureHotkeyRegistered = _captureHotkeyRegistered,
            settingsHotkeyRegistered = _settingsHotkeyRegistered,
            clickThrough = (styles & Native.WsExTransparent) != 0,
            noActivate = (styles & Native.WsExNoActivate) != 0,
            monitorCount = Forms.Screen.AllScreens.Length,
            displayChecks,
            bounds = new { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
            uiWorkingSetBytes = process.WorkingSet64
        }, SettingsStore.JsonOptions));
        var controls = new System.Windows.Media.Imaging.RenderTargetBitmap((int)_settingsWindow!.ActualWidth, (int)_settingsWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        controls.Render(_settingsWindow);
        var controlsEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        controlsEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(controls));
        using (var file = File.Create(Path.Combine(output, "controls.png"))) controlsEncoder.Save(file);
        for (int page = 1; page < 5; page++)
        {
            _settingsWindow.SelectPageForVerification(page);
            await Task.Delay(100);
            var render = new System.Windows.Media.Imaging.RenderTargetBitmap((int)_settingsWindow.ActualWidth, (int)_settingsWindow.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            render.Render(_settingsWindow);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
            using var file = File.Create(Path.Combine(output, $"controls-{page}.png")); png.Save(file);
        }
        _timer.Stop();
        _settings.MonitorMode = "Pinned"; _settings.PanelWidth = 300; _overlay.RefreshTheme();
        const string longName = "Application with a deliberately long name";
        _overlay.SetTarget(new ProcessChoice(42, longName, 0), false);
        await Task.Delay(100);
        bool captionValid = _overlay.VerifyPickerCaption(longName);
        bool popupReceivesInput = _overlay.VerifyPickerInputRouting();
        var pickerRender = new System.Windows.Media.Imaging.RenderTargetBitmap((int)_overlay.ActualWidth, (int)_overlay.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        pickerRender.Render(_overlay);
        var pickerEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        pickerEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(pickerRender));
        using (var file = File.Create(Path.Combine(output, "overlay-free.png"))) pickerEncoder.Save(file);
        File.WriteAllText(Path.Combine(output, "picker.json"), JsonSerializer.Serialize(new { captionValid, popupReceivesInput }));
        _application.Shutdown();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip { BackColor = Color.FromArgb(28, 28, 28), ForeColor = Color.FromArgb(230, 230, 230), ShowImageMargin = false };
        void Item(string text, Action action) => menu.Items.Add(text, null, (_, _) => _application.Dispatcher.Invoke(action));
        Item("Show / hide overlay     Ctrl+Alt+" + _settings.ToggleKey, ToggleOverlay);
        Item("Edit / lock placement    Ctrl+Alt+E", () => { _overlay.ShowPanel(); _overlay.SetEditing(!_overlay.Editing); });
        Item("Start / stop capture      Ctrl+Alt+R", ToggleCapture);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Settings…                    Ctrl+Alt+S", ShowSettings);
        Item("Enable FPS capture…", () => _ = EnableFpsAsync());
        Item("Restart standard collector", () => _ = RestartCollectorAsync(false));
        Item("Reload settings", () => { try { ApplySettings(SettingsStore.Load(), _manualPid); Notify(SettingsStore.LoadWarning ?? "Settings reloaded."); } catch (Exception e) { Notify(e.Message); } });
        Item("Open captures", () => { if (FolderLauncher.OpenCaptures() is { } error) Notify(error); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Quit Telemetry", () => _application.Shutdown());
        return menu;
    }

    private void RegisterHotkeys(string key)
    {
        bool toggle = TryRegisterToggle(key);
        _editHotkeyRegistered = Native.RegisterHotKey(_hotkeyWindow.Handle, 2, 0x4003, 'E');
        _captureHotkeyRegistered = Native.RegisterHotKey(_hotkeyWindow.Handle, 3, 0x4003, 'R');
        _settingsHotkeyRegistered = Native.RegisterHotKey(_hotkeyWindow.Handle, 5, 0x4003, 'S');
        if (!toggle || !_editHotkeyRegistered || !_captureHotkeyRegistered || !_settingsHotkeyRegistered) Notify("A Telemetry hotkey is already in use. Tray controls remain available; change the toggle key in Settings.");
    }
    private bool TryRegisterToggle(string key)
    {
        if (_registeredToggle == key) return true;
        int newId = _toggleHotkeyId == 1 ? 4 : 1;
        if (!Native.RegisterHotKey(_hotkeyWindow.Handle, newId, 0x4003, key[0])) return false;
        Native.UnregisterHotKey(_hotkeyWindow.Handle, _toggleHotkeyId);
        _toggleHotkeyId = newId; _registeredToggle = key; return true;
    }
    private nint WindowHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.WmHotkey)
        {
            switch (wParam.ToInt32()) { case 1: case 4: ToggleOverlay(); break; case 2: _overlay.ShowPanel(); _overlay.SetEditing(!_overlay.Editing); break; case 3: ToggleCapture(); break; case 5: ShowSettings(); break; }
            handled = true;
        }
        if ((uint)message == _taskbarCreated) { _tray.Visible = false; _tray.Visible = true; }
        return 0;
    }
    private void ToggleOverlay() { if (_overlay.IsVisible) _overlay.Hide(); else { _overlay.ShowPanel(); _overlay.Place(true); } }

    private async Task RestartCollectorAsync(bool elevated)
    {
        if (_restarting) return;
        _restarting = true;
        if (_capture is not null) StopCapture();
        _status = "Starting collector";
        lock (_dataGate) { _history.Clear(); _packet = null; }
        try
        {
            if (!await _collector.StartAsync(elevated) && elevated)
            {
                await _collector.StartAsync(false);
                Notify("FPS permission was not enabled. Standard hardware monitoring has resumed.");
            }
        }
        finally { _restarting = false; }
    }

    private void Receive(CollectorPacket packet)
    {
        CaptureWriter? capture;
        lock (_dataGate)
        {
            _packet = packet;
            foreach (var frame in packet.Frames) if (frame.ProcessId == _targetPid) _history.Add(frame);
            capture = _capture;
        }
        try { capture?.Write(packet); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { SettingsStore.Log(e); _application.Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_capture, capture)) StopCapture(); Notify("Capture stopped: disk write failed. The recording is incomplete."); }); }
        if (packet.FinishedCaptureId > 0)
            _application.Dispatcher.BeginInvoke(() =>
            {
                if (_capture is not null && _captureFinishId == packet.FinishedCaptureId) StopCapture(packet.CaptureDrainComplete);
            });
        Interlocked.Exchange(ref _lastPacket, Stopwatch.GetTimestamp());
    }

    private async Task TickAsync()
    {
        if (_disposed || _ticking) return;
        _ticking = true;
        try
        {
            var foreground = Native.Foreground();
            if (_captureTarget is not null && !_captureTarget.IsRunning) StopCapture();
            bool selectedExited = _manualTarget is not null && !_manualTarget.IsRunning;
            if (selectedExited && _capture is not null) StopCapture();
            int desired = _capture?.ProcessId ?? (selectedExited ? 0 : _manualPid > 0 ? _manualPid : foreground.Pid);
            if (selectedExited && _capture is null)
            {
                lock (_dataGate) { _targetPid = 0; _history.Clear(); }
                _targetName = _manualTarget!.Name + " · exited";
            }
            else if (desired != Environment.ProcessId && desired > 0)
            {
                if (desired != _candidatePid) { _candidatePid = desired; _candidateTicks = 0; }
                if (++_candidateTicks >= 2 && desired != _targetPid)
                {
                    lock (_dataGate) { _targetPid = desired; _history.Clear(); }
                    try { using var p = Process.GetProcessById(desired); _targetName = p.ProcessName; }
                    catch (ArgumentException) { _targetName = "Exited process"; }
                }
            }
            if (!_restarting) await _collector.TargetAsync(new(_targetPid, _targetName, _overlay.IsVisible || _capture is not null, _captureFinishId));
            if (_overlay.IsVisible) _overlay.Place();
            CollectorPacket? packet;
            FrameSample[] frames;
            lock (_dataGate) { packet = _packet; frames = _history.Items().ToArray(); }
            if (packet is not null && packet.Sensors.ProcessId != _targetPid)
                packet = packet with { Sensors = packet.Sensors with { ProcessCpu = null, ProcessRamGb = null } };
            double nowMs = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            var window = FrameWindow.Calculate(frames, nowMs, _targetPid);
            _frameWindow = window;
            bool stale = packet is null || Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPacket)).TotalSeconds > 3;
            if (packet is not null && !stale) _status = packet.DroppedFrames > 0 ? $"Capture incomplete: {packet.DroppedFrames} lost events/samples" : packet.FrameStatus;
            if (frames.Length == 32768 && frames[0].TimestampMs >= nowMs - 10_000) _status = "Live window capped: 32,768 samples";
            if (_captureFinishId > 0) _status = "Finishing capture: draining trace buffers…";
            if (selectedExited) _status = "Application exited · choose another";
            _overlay.SetTarget(_manualTarget, _capture is not null);
            _overlay.Update(packet, window, _targetName, _status, _capture is not null, stale);
            if (_captureFinishId > 0 && Stopwatch.GetElapsedTime(_captureStopStarted).TotalSeconds > 8) StopCapture();
            if (_captureFinishId == 0 && _capture is { } capture && (capture.LimitReached || DateTimeOffset.UtcNow - capture.Started >= TimeSpan.FromHours(1))) RequestCaptureStop();
        }
        finally { _ticking = false; }
    }

    private void ToggleCapture()
    {
        if (_capture is not null) { RequestCaptureStop(); return; }
        if (_manualTarget is not null && !_manualTarget.IsRunning) { Notify("The selected application has exited. Choose another application."); return; }
        var foreground = Native.Foreground();
        int target = _manualPid > 0 ? _manualPid : foreground.Pid;
        if (!_restarting && target > 0 && target != Environment.ProcessId && target != _targetPid)
        {
            try
            {
                using var process = Process.GetProcessById(target);
                lock (_dataGate) { _targetPid = target; _targetName = process.ProcessName; _history.Clear(); }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { Notify("The selected application is no longer available."); return; }
        }
        if (_targetPid <= 0 || _restarting) { Notify("Focus a graphics application first."); return; }
        var identity = ProcessChoice.Read(_targetPid);
        if (identity is null) { Notify("The application exited or cannot be inspected."); return; }
        try { lock (_dataGate) { _capture = new(_targetPid, _targetName, _packet?.DroppedFrames ?? 0); _captureTarget = identity; } Notify("Recording " + _targetName + ". Ctrl+Alt+R saves the capture."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notify(e.Message); SettingsStore.Log(e); }
    }
    private void RequestCaptureStop()
    {
        if (_capture is null || _captureFinishId > 0) return;
        _capture.RequestStop();
        _captureStopStarted = Stopwatch.GetTimestamp();
        _captureFinishId = ++_captureSequence;
    }
    private void StopCapture(bool drained = false)
    {
        CaptureWriter? capture;
        lock (_dataGate)
        {
            capture = _capture; _capture = null; _captureTarget = null; _captureFinishId = 0;
        }
        if (capture is null) return;
        capture.RequestStop(); capture.ConfirmDrain(drained);
        _captureSaves.RemoveAll(task => task.IsCompleted);
        _captureSaves.Add(Task.Run(() =>
        {
            string message;
            try { capture.Dispose(); message = drained ? "Capture saved. Check summary.json for completeness, sample count and event loss." : "Capture saved as incomplete: final frame delivery was not confirmed."; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { SettingsStore.Log(e); message = "Could not finalize capture. Raw files may still be available."; }
            if (!_disposed) _application.Dispatcher.BeginInvoke(() => Notify(message));
        }));
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.ShowControls(); return; }
        var s = _packet?.Sensors;
        string Diagnostics()
        {
            var sensors = _packet?.Sensors;
            return $"CPU  {sensors?.CpuName ?? "Unavailable"}\nGPU  {sensors?.GpuName ?? "Unavailable"}\n\n{sensors?.SensorStatus ?? _status}\n\n{_status}\nCollector: {(_packet?.Elevated == true ? "FPS permission enabled" : "Standard user")}\nDropped trace events: {_packet?.DroppedFrames ?? 0}\n\nTarget: {_targetName} · PID {_targetPid}\nSelection: {(_manualTarget is null ? "Automatic" : "Pinned application")}\nFrame age: {(_frameWindow?.LatestFrameAgeMs / 1000)?.ToString("0.00") ?? "—"} s\nCollector packet age: {Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastPacket)).TotalSeconds:0.00} s\nPresentation surface: {_frameWindow?.SwapChain ?? "—"}\nLive samples: {_frameWindow?.Statistics?.Count ?? 0}\nP99 frame time: {_frameWindow?.Statistics?.P99Ms.ToString("0.00") ?? "—"} ms\n\nCPU package temperature: not available via Windows\nGPU temperature: {sensors?.GpuTemperature?.ToString("0") ?? "—"} °C\nGPU slowdown threshold: {sensors?.GpuThermalLimit?.ToString("0") ?? "—"} °C\nGPU power limit: {sensors?.GpuPowerLimit?.ToString("0") ?? "—"} W\nVRAM reserved by driver: {((sensors?.VramReservedGb * 1024)?.ToString("0") ?? "unavailable (legacy VRAM may include reservation)")} MiB\nSystem committed memory: {sensors?.CommitUsedGb?.ToString("0.0") ?? "—"} / {sensors?.CommitTotalGb?.ToString("0.0") ?? "—"} GiB";
        }
        _settingsWindow = new(_settings, ApplySettings, () => _ = EnableFpsAsync(), Diagnostics, ToggleOverlay, () => { _overlay.ShowPanel(); _overlay.SetEditing(!_overlay.Editing); }, ToggleCapture, () => _manualPid);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.ShowControls();
    }
    private void ApplySettings(AppSettings candidate, int manualPid)
    {
        candidate.Validate();
        if (_capture is not null && (manualPid != _manualPid || candidate.MonitorMode != _settings.MonitorMode))
            throw new InvalidOperationException("Stop the recording before changing the application or mode.");
        var selection = manualPid == _manualPid ? _manualTarget : manualPid > 0 ? ProcessChoice.Read(manualPid) : null;
        if (manualPid > 0 && selection is null) throw new InvalidOperationException("That application exited or cannot be inspected. Refresh the list.");
        if (candidate.ToggleKey is "E" or "R" or "S") throw new InvalidOperationException("E, R and S are reserved for placement, recording and settings.");
        if (!TryRegisterToggle(candidate.ToggleKey)) throw new InvalidOperationException("That shortcut is in use. Your existing shortcut is still active.");
        string? priorStartup = SettingsStore.GetStartupCommand();
        try { SettingsStore.SetStartup(candidate.StartAtSignIn); SettingsStore.Save(candidate, allowRecovery: true); }
        catch
        {
            try { SettingsStore.RestoreStartupCommand(priorStartup); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { SettingsStore.Log(e); }
            TryRegisterToggle(_settings.ToggleKey); throw;
        }
        foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite)) property.SetValue(_settings, property.GetValue(candidate));
        SelectTarget(candidate.MonitorMode == "Foreground" ? null : selection);
        _tray.Text = "Telemetry · Ctrl+Alt+" + _settings.ToggleKey;
        var oldMenu = _tray.ContextMenuStrip; _tray.ContextMenuStrip = BuildMenu(); oldMenu?.Dispose();
        _overlay.RefreshTheme();
    }
    private void SelectTarget(ProcessChoice? choice)
    {
        if (_manualTarget == choice) return;
        _manualTarget = choice; _manualPid = choice?.Pid ?? 0;
        _candidatePid = 0; _candidateTicks = 0;
        lock (_dataGate)
        {
            _targetPid = choice?.Pid ?? 0;
            _targetName = choice?.Name ?? "Active application";
            _history.Clear();
        }
        _overlay.SetTarget(choice, _capture is not null);
    }
    private void SaveSettings() { if (_testing) return; try { SettingsStore.Save(_settings); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Notify(e.Message); } }
    private void Notify(string text) { _tray.BalloonTipTitle = "Telemetry"; _tray.BalloonTipText = text; _tray.ShowBalloonTip(3500); }
    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var pen = new Pen(Color.FromArgb(135, 175, 175), 2.6f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            g.DrawLines(pen, new[] { new System.Drawing.Point(3, 18), new(9, 18), new(13, 7), new(19, 25), new(23, 14), new(29, 14) });
        }
        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var icon = (Icon)temporary.Clone(); Native.DestroyIcon(handle); return icon;
    }
    public void Dispose()
    {
        _disposed = true; _timer.Stop(); StopCapture(); _collector.Dispose();
        Task.WaitAll(_captureSaves.ToArray());
        for (int id = 1; id <= 5; id++) Native.UnregisterHotKey(_hotkeyWindow.Handle, id);
        _hotkeyWindow.Dispose(); _tray.Visible = false; _tray.Dispose(); _icon.Dispose(); _overlay.Close();
    }
}
