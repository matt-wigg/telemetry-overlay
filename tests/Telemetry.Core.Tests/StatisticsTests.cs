using Xunit;

namespace Telemetry.Core.Tests;

public class StatisticsTests
{
    [Fact]
    public void FrameRateUsesElapsedTimeRatherThanMeanOfInstantaneousFps()
    {
        var statistics = FrameStatistics.Calculate([5, 15])!;
        Assert.Equal(100, statistics.Fps, 8);
        Assert.Equal(1000.0 / 15, statistics.OnePercentLow, 8);
        Assert.Equal(15, statistics.P99Ms);
    }
    [Fact]
    public void LowUsesSlowestOnePercentAndP99UsesNearestRank()
    {
        var samples = Enumerable.Repeat(10.0, 198).Concat([20.0, 40.0]);
        var s = FrameStatistics.Calculate(samples)!;
        Assert.Equal(1000.0 / 30, s.OnePercentLow, 8);
        Assert.Equal(10, s.P99Ms);
        Assert.Equal(40, s.MaxMs);
    }
    [Fact] public void InvalidValuesAreExcluded() => Assert.Equal(2, FrameStatistics.Calculate([0, -1, double.NaN, double.PositiveInfinity, 10, 10])!.Count);
    [Fact] public void InsufficientDataIsUnavailable() => Assert.Null(FrameStatistics.Calculate([0, 10]));
    [Fact]
    public void RingBufferEvictsOldestAndPreservesOrder()
    {
        var ring = new RingBuffer<int>(3);
        for (int i = 0; i < 10000; i++) ring.Add(i);
        Assert.Equal(new[] { 9997, 9998, 9999 }, ring.Items());
        ring.Clear(); Assert.Empty(ring.Items());
        ring.Add(42); Assert.Equal(new[] { 42 }, ring.Items());
    }
    [Fact]
    public void HeavyUtilizationIsBusyRatherThanCritical()
    {
        var latch = new SeverityLatch();
        for (int i = 0; i < 3; i++) latch.Update(100, 90, 98, true);
        Assert.Equal(Severity.Busy, latch.Update(100, 90, 98, true));
    }
    [Fact]
    public void ThermalWarningRequiresPersistenceAndHasHysteresis()
    {
        var latch = new SeverityLatch();
        Assert.Equal(Severity.Normal, latch.Update(86, 85, 95));
        Assert.Equal(Severity.Normal, latch.Update(86, 85, 95));
        Assert.Equal(Severity.Warning, latch.Update(86, 85, 95));
        for (int i = 0; i < 5; i++) Assert.Equal(Severity.Warning, latch.Update(84, 85, 95));
        for (int i = 0; i < 3; i++) latch.Update(82, 85, 95);
        Assert.Equal(Severity.Normal, latch.Update(82, 85, 95));
        Assert.Equal(Severity.Unavailable, latch.Update(null, 85, 95));
    }
    [Fact]
    public void SettingsRejectDangerousOrUnboundedValues()
    {
        Assert.Throws<InvalidDataException>(() => new AppSettings { Scale = double.NaN }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { CpuWarningC = 99, CpuCriticalC = 90 }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { MetricOrder = ["GPU", "GPU"] }.Validate());
        Assert.Throws<InvalidDataException>(() => new AppSettings { SchemaVersion = 999 }.Validate());
        new AppSettings().Validate();
    }
}
