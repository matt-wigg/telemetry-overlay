using Xunit;

namespace Telemetry.Core.Tests;

public class DeliveryTests
{
    [Fact]
    public void RetiredDominantChainDoesNotHideItsLiveReplacement()
    {
        var old = Enumerable.Range(0, 100).Select(i => new FrameSample(42, "old", 6000 + i * 10, 10, null, null, "test"));
        FrameSample[] replacement = [new(42, "new", 9800, 20, null, null, "test"), new(42, "new", 9900, 20, null, null, "test")];
        var window = FrameWindow.Calculate(old.Concat(replacement), 10000, 42);
        Assert.Equal("new", window.SwapChain);
        Assert.Equal(50, window.Statistics!.Fps);
        Assert.Equal(2, window.Statistics.Count);
        Assert.Equal(100, window.LatestFrameAgeMs);
        Assert.DoesNotContain(10, window.Graph); // Never mix the two presentation surfaces.
    }

    [Fact]
    public void LiveReplacementNeedsTwoIntervalsAndExpiresWhenItStops()
    {
        FrameSample[] samples = [new(42, "old", 6000, 10, null, null, "test"), new(42, "old", 6100, 10, null, null, "test"),
            new(42, "new", 9900, 20, null, null, "test")];
        Assert.Null(FrameWindow.Calculate(samples, 10000, 42).Statistics);
        var complete = samples.Append(new FrameSample(42, "new", 10000, 20, null, null, "test")).ToArray();
        Assert.NotNull(FrameWindow.Calculate(complete, 12499, 42).Statistics);
        Assert.Null(FrameWindow.Calculate(complete, 12500, 42).Statistics);
    }

    [Fact]
    public void EqualActiveChainsHaveStableSelectionRegardlessOfDeliveryOrder()
    {
        FrameSample[] samples = [new(42, "B", 9900, 20, null, null, "test"), new(42, "A", 9900, 10, null, null, "test"),
            new(42, "B", 9950, 20, null, null, "test"), new(42, "A", 9950, 10, null, null, "test")];
        Assert.Equal("A", FrameWindow.Calculate(samples, 10000, 42).SwapChain);
        Assert.Equal("A", FrameWindow.Calculate(samples.Reverse(), 10000, 42).SwapChain);
    }

    [Fact]
    public void LateDeliveryFillsHistoricalBucketsAndStaleStatisticsExpire()
    {
        FrameSample F(int pid, double time, double duration) => new(pid, "A", time, duration, null, null, "test");
        FrameSample[] samples = [F(42, 9000, 10), F(42, 9100, 20), F(43, 9200, 999), F(42, 11000, 999)];
        var window = FrameWindow.Calculate(samples, 10000, 42);
        Assert.Equal(1000.0 / 15, window.Statistics!.Fps, 8);
        Assert.Equal(20, window.Graph[58]);
        Assert.True(double.IsNaN(window.Graph[59]));
        Assert.Null(FrameWindow.Calculate(samples.Take(2), 12000, 42).Statistics);
    }
    [Fact]
    public async Task BoundedReaderPreservesBufferedLinesAndFinalPartialLine()
    {
        var reader = new BoundedLineReader(new StringReader("one\r\ntwo\nlast"), 8);
        Assert.Equal("one", await reader.ReadLineAsync());
        Assert.Equal("two", await reader.ReadLineAsync());
        Assert.Equal("last", await reader.ReadLineAsync());
        Assert.Null(await reader.ReadLineAsync());
    }
    [Fact]
    public async Task BoundedReaderRejectsOversizedMessagesAndHonorsCancellation()
    {
        var reader = new BoundedLineReader(new StringReader(new string('x', 9000)), 8192);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ReadLineAsync());
        var cancelled = new BoundedLineReader(new StringReader("hello"), 8);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await cancelled.ReadLineAsync(new CancellationToken(true)));
    }
    [Fact]
    public void SettingsRejectNullCollectionsAndRemoteFonts()
    {
        Assert.Throws<InvalidDataException>(() => new AppSettings { MetricOrder = null! }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Theme = null! }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Placements = null! }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { Theme = new() { FontFamily = "https://example.invalid/font" } }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { PanelHeight = 420 }.Validate());
    }
}
