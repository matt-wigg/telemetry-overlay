using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Telemetry.Core;
using Xunit;

namespace Telemetry.App.Tests;

public sealed class WidgetRenderTests
{
    [Theory]
    [InlineData(300, 487, 1.0, "live")]
    [InlineData(360, 554, 1.0, "live")]
    [InlineData(300, 487, 2.0, "live")]
    [InlineData(300, 487, 1.0, "waiting")]
    [InlineData(300, 487, 1.0, "stale")]
    public async Task RendersSyntheticFixtureWithoutOpeningAWindow(int width, int height, double scale, string state)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings { PanelWidth = width, PanelHeight = height, Scale = scale, MonitorMode = "Pinned" };
                var widget = new OverlayWindow(settings);
                widget.RefreshTheme();
                const string target = "Example application with a long name";
                widget.SetTarget(new ProcessChoice(42, target, 0), false);
                var graph = Enumerable.Range(0, 60).Select(i => i is 18 or 19 ? double.NaN : 2 + Math.Abs(Math.Sin(i * .7)) * 1.8 + (i % 17 == 0 ? 20 : 0)).ToArray();
                if (state != "live") Array.Fill(graph, double.NaN, 54, 6);
                var statistics = state == "live" ? new FrameStatistics(5922, 592.2, 451.4, 3.1, 1.69, 22) : null;
                var packet = new CollectorPacket(new SensorSnapshot
                {
                    CpuLoad = 24, GpuLoad = 97, GpuTemperature = 67, GpuPower = 290, GpuPowerLimit = 360,
                    GpuClock = 2780, GpuFan = 38, VramUsedGb = 9.2, VramTotalGb = 16, RamUsedGb = 19.2,
                    RamTotalGb = 64, ProcessCpu = 21, ProcessRamGb = 3.1
                }, [], "DirectX · application present cadence", 0, true);
                widget.Update(packet, new FrameWindow(statistics, graph, "sample", state == "live" ? 500 : 3000),
                    target, packet.FrameStatus, false, state == "stale");
                var surface = widget.PreparePreview(width * scale, height * scale);
                Assert.Equal(nint.Zero, new WindowInteropHelper(widget).Handle);
                Assert.True(widget.VerifyPickerCaption(target));
                var image = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96, 96, PixelFormats.Pbgra32);
                image.Render(surface);
                Assert.Equal((int)(width * scale), image.PixelWidth);
                Assert.Equal((int)(height * scale), image.PixelHeight);
                if (Environment.GetEnvironmentVariable("TELEMETRY_RENDER_OUTPUT") is { Length: > 0 } output)
                {
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(Path.Combine(output, $"widget-{width}-{scale:0}-{state}.png"));
                    encoder.Save(file);
                }
                widget.Close();
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
