using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Telemetry.Core;
using Xunit;

namespace Telemetry.App.Tests;

public sealed class CaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Telemetry-tests-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void EmptyRecordingIsNotACompleteBenchmark()
    {
        using var capture = new CaptureWriter(42, "test", 0, _root);
        capture.Dispose();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("statistics").ValueKind);
    }
    [Fact]
    public void RecordingFiltersOtherProcessesAndPreRecordingIntervals()
    {
        double now = 1000;
        using var capture = new CaptureWriter(42, "test", 0, _root, () => now);
        FrameSample F(int pid, double time) => new(pid, "A", time, 10, null, null, "ApplicationPresent");
        capture.Write(new(new SensorSnapshot { ProcessId = 42 }, [F(42, now - 100), F(43, now + 100), F(42, now + 100), F(42, now + 110)], "ok", 0, true));
        now = 1200;
        capture.ConfirmDrain(true);
        capture.Dispose();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Assert.Equal(2, json.RootElement.GetProperty("capturedFrames").GetInt32());
        Assert.True(json.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(100, json.RootElement.GetProperty("statistics").GetProperty("Fps").GetDouble());
        Assert.Equal(3, File.ReadAllLines(Path.Combine(capture.DirectoryPath, "frames.csv")).Length);
        var sensors = File.ReadAllLines(Path.Combine(capture.DirectoryPath, "sensors.csv"));
        Assert.Equal(sensors[0].Split(',').Length, sensors[1].Split(',').Length);
        Assert.Contains("commit_limit_gib", sensors[0]);
        Assert.Contains("process_id", sensors[0]);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopAcceptsDelayedFramesWithinBoundaryAndRequiresDrain(bool drained)
    {
        double clock = 1000;
        using var capture = new CaptureWriter(42, "test", 0, _root, () => clock);
        clock = 1100;
        capture.RequestStop();
        FrameSample F(double time) => new(42, "A", time, 10, null, null, "ApplicationPresent");
        capture.Write(new(new SensorSnapshot(), [F(1005), F(1020), F(1100), F(1110)], "ok", 0, true));
        capture.ConfirmDrain(drained);
        capture.Dispose();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Assert.Equal(2, json.RootElement.GetProperty("capturedFrames").GetInt32());
        Assert.Equal(drained, json.RootElement.GetProperty("complete").GetBoolean());
    }
    [Fact]
    public void SeparateSwapChainsDoNotInventEnoughDataForStatistics()
    {
        using var capture = new CaptureWriter(42, "test", 0, _root, () => 1000);
        capture.Write(new(new SensorSnapshot(), [new(42, "A", 1020, 10, null, null, "test"), new(42, "B", 1020, 10, null, null, "test")], "ok", 0, true));
        capture.ConfirmDrain(true);
        capture.Dispose();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }
    [Fact]
    public void TraceLossIsRelativeToCaptureStartAndMarksIncomplete()
    {
        using var capture = new CaptureWriter(42, "test", 7, _root);
        capture.Write(new(new SensorSnapshot(), [], "loss", 10, true));
        capture.Dispose();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture.DirectoryPath, "summary.json")));
        Assert.Equal(3, json.RootElement.GetProperty("droppedEventsOrSamples").GetInt32());
        Assert.False(json.RootElement.GetProperty("complete").GetBoolean());
    }
    [Theory]
    [InlineData("TraceProperties", 120)]
    [InlineData("TraceLogfile", 448)]
    [InlineData("EventHeader", 80)]
    [InlineData("EventRecord", 112)]
    [InlineData("EnableParameters", 48)]
    public void NativeEtwLayoutsMatchWindowsX64Abi(string name, int bytes)
    {
        Assert.Equal(8, IntPtr.Size);
        var type = typeof(FrameProvider).GetNestedType(name, BindingFlags.NonPublic)!;
        Assert.Equal(bytes, Marshal.SizeOf(type));
    }
    public void Dispose()
    {
        // Only a freshly created, uniquely named test directory under the OS temp directory.
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
