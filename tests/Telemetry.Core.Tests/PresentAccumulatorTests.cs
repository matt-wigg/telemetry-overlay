using Xunit;

namespace Telemetry.Core.Tests;

public sealed class PresentAccumulatorTests
{
    [Fact]
    public void MeasuresStartToStartRatherThanTimeInsidePresent()
    {
        var data = new PresentAccumulator(10_000_000);
        data.Start(true, 1, 0x12, 1_000_000, false);
        Assert.Null(data.Stop(true, 1, 42, 0));
        data.Start(true, 1, 0x12, 1_166_667, false);
        var frame = data.Stop(true, 1, 42, 0)!;
        Assert.Equal(16.6667, frame.DurationMs, 5);
        Assert.Equal(116.6667, frame.TimestampMs, 5);
        Assert.Equal(42, frame.ProcessId);
    }
    [Theory]
    [InlineData(-1)] // Failed present.
    [InlineData(0x087A0001)] // DXGI_STATUS_OCCLUDED: success HRESULT, but no presented frame.
    public void UnsuccessfulOrOccludedCallsDoNotBecomeFrames(int result)
    {
        var data = new PresentAccumulator(1000);
        data.Start(true, 1, 1, 10, false); data.Stop(true, 1, 1, 0);
        data.Start(true, 1, 1, 20, false); Assert.Null(data.Stop(true, 1, 1, result));
        data.Start(true, 1, 1, 30, false); Assert.Equal(20, data.Stop(true, 1, 1, 0)!.DurationMs);
    }
    [Fact]
    public void TestCallsCannotCompleteAStalePendingPresent()
    {
        var data = new PresentAccumulator(1000);
        data.Start(true, 1, 1, 10, false);
        data.Start(true, 1, 1, 20, true);
        Assert.Null(data.Stop(true, 1, 1, 0));
        data.Start(true, 1, 1, 30, false);
        Assert.Null(data.Stop(true, 1, 1, 0));
    }
    [Fact]
    public void SwapChainsAndRuntimesStaySeparate()
    {
        var data = new PresentAccumulator(1000);
        data.Start(true, 1, 1, 10, false); data.Stop(true, 1, 1, 0);
        data.Start(false, 1, 1, 20, false); Assert.Null(data.Stop(false, 1, 1, 0));
        data.Start(true, 1, 2, 30, false); Assert.Null(data.Stop(true, 1, 1, 0));
        data.Start(true, 2, 1, 40, false); Assert.Equal(30, data.Stop(true, 2, 1, 0)!.DurationMs);
    }
    [Fact]
    public void ProcessSwitchResetsBaselinesAndPendingCalls()
    {
        var data = new PresentAccumulator(1000);
        data.Start(true, 1, 1, 10, false); data.Stop(true, 1, 1, 0);
        data.Start(true, 1, 1, 20, false); data.Clear();
        Assert.Null(data.Stop(true, 1, 2, 0));
        data.Start(true, 1, 1, 30, false); Assert.Null(data.Stop(true, 1, 2, 0));
    }
    [Fact]
    public void OutOfOrderCompletionCannotMoveBaselineBackwards()
    {
        var data = new PresentAccumulator(1000);
        data.Start(true, 1, 1, 20, false); data.Stop(true, 1, 1, 0);
        data.Start(true, 1, 1, 10, false); Assert.Null(data.Stop(true, 1, 1, 0));
        data.Start(true, 1, 1, 30, false); Assert.Equal(10, data.Stop(true, 1, 1, 0)!.DurationMs);
    }
    [Fact]
    public void CapacityRejectionsAreReported()
    {
        var data = new PresentAccumulator(1000);
        for (uint thread = 0; thread < 300; thread++) data.Start(true, thread, 1, 1, false);
        Assert.Equal(44, data.RejectedForCapacity);
    }
}
