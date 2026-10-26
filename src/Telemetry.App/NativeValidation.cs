using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Telemetry.App;

/// <summary>Opt-in, finite DirectX verification. Never run during ordinary application startup.</summary>
internal static class NativeValidation
{
    internal sealed record Reference(string SwapChain, double[] SuccessfulPresentTimesMs);

    internal static async Task<int> VerifyAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var packets = new List<CollectorPacket>();
        var gate = new object();
        using var collector = new CollectorClient();
        collector.Packet += packet => { lock (gate) packets.Add(packet); };
        if (!await collector.StartAsync(true))
        {
            File.WriteAllText(Path.Combine(directory, "fps-validation.json"), "{\"passed\":false,\"reason\":\"Windows permission was not approved\"}");
            return 2;
        }
        string referencePath = Path.Combine(directory, "reference.json");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--frame-workload"); start.ArgumentList.Add(referencePath);
        using var workload = Process.Start(start) ?? throw new InvalidOperationException("Could not launch validation workload.");
        await collector.TargetAsync(new(workload.Id, "DirectX validation"));
        await workload.WaitForExitAsync();
        await collector.TargetAsync(new(workload.Id, "DirectX validation", FinishCaptureId: 1));
        bool drained = false;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            lock (gate) drained = packets.Any(p => p.FinishedCaptureId == 1 && p.CaptureDrainComplete);
            if (drained) break;
            await Task.Delay(100);
        }
        var reference = JsonSerializer.Deserialize<Reference>(File.ReadAllText(referencePath))!;
        CollectorPacket[] snapshot;
        lock (gate) snapshot = packets.ToArray();
        var frames = snapshot.SelectMany(p => p.Frames).Where(f => f.SwapChain == reference.SwapChain).ToArray();
        var times = reference.SuccessfulPresentTimesMs;
        var errors = new List<double>();
        foreach (var frame in frames)
        {
            int index = Array.BinarySearch(times, frame.TimestampMs);
            if (index < 0) index = ~index - 1;
            if (index < 1 || index >= times.Length || frame.TimestampMs - times[index] > 2) continue;
            errors.Add(Math.Abs(frame.DurationMs - (times[index] - times[index - 1])));
        }
        double? maxError = errors.Count > 0 ? errors.Max() : null;
        long lost = snapshot.Select(p => p.DroppedFrames).DefaultIfEmpty().Max();
        bool passed = drained && frames.Length >= 200 && errors.Count == frames.Length && maxError < 1 && lost == 0;
        File.WriteAllText(Path.Combine(directory, "fps-validation.json"), JsonSerializer.Serialize(new
        {
            passed,
            finalDrainConfirmed = drained,
            referencePresents = times.Length,
            capturedIntervals = frames.Length,
            matchedIntervals = errors.Count,
            maxIntervalErrorMs = maxError,
            meanIntervalErrorMs = errors.Count > 0 ? errors.Average() : (double?)null,
            droppedEventsOrSamples = lost,
            statistics = FrameStatistics.Calculate(frames.Select(f => f.DurationMs)),
            status = snapshot.LastOrDefault()?.FrameStatus,
            elevated = snapshot.LastOrDefault()?.Elevated,
            scope = "DXGI D3D11 successful Present-call cadence; not display latency or broad game compatibility"
        }, SettingsStore.JsonOptions));
        return passed ? 0 : 1;
    }

    [STAThread]
    internal static int RunWorkload(string output)
    {
        var application = new Application();
        using var window = new HwndSource(new HwndSourceParameters("Telemetry / FPS verification — closes automatically")
        { Width = 480, Height = 180, PositionX = 60, PositionY = 60, WindowStyle = 0x10CF0000 });
        using var graphics = new DxgiWorkload(window.Handle);
        var samples = new List<double>(500);
        var elapsed = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        timer.Tick += (_, _) =>
        {
            if (elapsed.Elapsed.TotalSeconds < 3) return;
            if (elapsed.Elapsed.TotalSeconds >= 15)
            {
                timer.Stop();
                File.WriteAllText(output, JsonSerializer.Serialize(new Reference(graphics.Identity, samples.ToArray())));
                application.Shutdown(); return;
            }
            double timestamp = Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
            if (graphics.Present() == 0) samples.Add(timestamp);
        };
        timer.Start(); application.Run(); return 0;
    }

    private sealed unsafe class DxgiWorkload : IDisposable
    {
        private nint _swapChain, _device, _context;
        internal string Identity => ((ulong)_swapChain).ToString("X");
        internal DxgiWorkload(nint window)
        {
            var description = new SwapChainDescription
            {
                Width = 480,
                Height = 180,
                RefreshDenominator = 1,
                Format = 28,
                SampleCount = 1,
                BufferUsage = 0x20,
                BufferCount = 2,
                OutputWindow = window,
                Windowed = 1,
                SwapEffect = 0
            };
            int result = D3D11CreateDeviceAndSwapChain(0, 1, 0, 0, 0, 0, 7, ref description,
                out _swapChain, out _device, out _, out _context);
            if (result < 0) { Dispose(); Marshal.ThrowExceptionForHR(result); }
        }
        internal int Present() => ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)(*(nint**)_swapChain)[8])(_swapChain, 0, 0);
        public void Dispose()
        {
            static void Release(ref nint instance)
            {
                if (instance != 0) { ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance); instance = 0; }
            }
            Release(ref _swapChain); Release(ref _context); Release(ref _device);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct SwapChainDescription
        {
            public uint Width, Height, RefreshNumerator, RefreshDenominator, Format, ScanlineOrdering, Scaling;
            public uint SampleCount, SampleQuality, BufferUsage, BufferCount;
            public nint OutputWindow;
            public int Windowed;
            public uint SwapEffect, Flags;
        }
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDeviceAndSwapChain(nint adapter, uint driver, nint software, uint flags,
            nint levels, uint levelCount, uint sdk, ref SwapChainDescription description, out nint swapChain,
            out nint device, out uint featureLevel, out nint context);
    }
}
