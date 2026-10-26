using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Text.Json;

namespace Telemetry.App;

internal static class CollectorHost
{
    internal static async Task RunAsync(string pipeName)
    {
        // The elevated helper accepts only a random pipe name, never executable or output paths.
        if (!pipeName.StartsWith("Telemetry-", StringComparison.Ordinal) || pipeName.Length > 80 || pipeName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pipe.ConnectAsync(timeout.Token);
        if (!Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid)) throw new IOException("Cannot identify collector host.");
        using (var server = Process.GetProcessById((int)serverPid))
            if (!string.Equals(server.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Collector host executable does not match.");
        using var reader = new StreamReader(pipe, leaveOpen: true);
        var lines = new BoundedLineReader(reader, 1024);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var cancellation = new CancellationTokenSource();
        CollectorCommand command = new(0, "Desktop");
        var commands = Task.Run(async () =>
        {
            try
            {
                while (await lines.ReadLineAsync(cancellation.Token) is { } line)
                {
                    var parsed = JsonSerializer.Deserialize<CollectorCommand>(line);
                    if (parsed is not null && parsed.ProcessId >= 0) Volatile.Write(ref command, parsed);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or ObjectDisposedException) { }
            finally { await cancellation.CancelAsync(); }
        });
        using var sensors = new SensorProvider();
        using var frames = new FrameProvider();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        long lastFinishId = 0;
        try
        {
            do
            {
                var target = Volatile.Read(ref command);
                frames.Target(target.CollectFrames ? target.ProcessId : 0);
                long finishedId = 0;
                bool drained = false;
                if (target.FinishCaptureId > 0 && target.FinishCaptureId != lastFinishId)
                {
                    drained = frames.FinishCapture();
                    finishedId = lastFinishId = target.FinishCaptureId;
                }
                var data = frames.Drain();
                var packet = new CollectorPacket(sensors.Read(target.ProcessId), data.Frames, data.Status, data.Dropped, elevated, finishedId, drained);
                await writer.WriteLineAsync(JsonSerializer.Serialize(packet).AsMemory(), cancellation.Token);
            } while (await timer.WaitForNextTickAsync(cancellation.Token));
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { await cancellation.CancelAsync(); await commands; }
    }
}

internal sealed class CollectorClient : IDisposable
{
    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private Process? _process;
    private readonly SemaphoreSlim _writeGate = new(1);
    private CancellationTokenSource? _cancellation;
    internal event Action<CollectorPacket>? Packet;
    internal event Action<string>? Status;

    internal async Task<bool> StartAsync(bool elevated)
    {
        Stop();
        var cancellation = _cancellation = new();
        string name = "Telemetry-" + Guid.NewGuid().ToString("N");
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Windows user identity unavailable.");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        var pipe = _pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 65536, 65536, security);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = elevated, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (elevated) start.Verb = "runas";
        start.ArgumentList.Add("--collector"); start.ArgumentList.Add(name);
        try
        {
            _process = Process.Start(start);
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            await pipe.WaitForConnectionAsync(connectionTimeout.Token);
            if (_process is null || !Native.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientPid) || clientPid != _process.Id)
                throw new IOException("Unexpected collector process connected.");
            _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            _ = Task.Run(async () =>
            {
                try
                {
                    using var reader = new StreamReader(pipe, leaveOpen: true);
                    var lines = new BoundedLineReader(reader, 8_000_000);
                    while (await lines.ReadLineAsync(cancellation.Token) is { } line)
                    {
                        if (ReferenceEquals(_pipe, pipe) && JsonSerializer.Deserialize<CollectorPacket>(line) is { } data) Packet?.Invoke(data);
                    }
                    if (!cancellation.IsCancellationRequested) Status?.Invoke("Collector disconnected. Restart from tray.");
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or JsonException or ObjectDisposedException)
                { if (!cancellation.IsCancellationRequested) Status?.Invoke("Collector stopped. Restart from tray."); }
            }, cancellation.Token);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or OperationCanceledException or IOException or UnauthorizedAccessException)
        { Status?.Invoke("Collector could not start; permission may have been declined."); Stop(); SettingsStore.Log(e); return false; }
    }

    internal async Task TargetAsync(CollectorCommand command)
    {
        if (!await _writeGate.WaitAsync(0)) return;
        try { if (_writer is not null) await _writer.WriteLineAsync(JsonSerializer.Serialize(command)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        finally { _writeGate.Release(); }
    }

    private void Stop()
    {
        _cancellation?.Cancel();
        try { _writer?.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        _writer = null;
        _pipe?.Dispose(); _pipe = null;
        // Closing the pipe asks even an elevated helper to exit and dispose its own children.
        _process?.Dispose(); _process = null;
        _cancellation?.Dispose(); _cancellation = null;
    }
    public void Dispose() => Stop();
}
