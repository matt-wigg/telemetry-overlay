using Xunit;

namespace Telemetry.Core.Tests;

public class PanelLayoutTests
{
    [Fact]
    public void FormerPlacementShortcutCanBeUsedForVisibility() => new AppSettings { ToggleKey = "E" }.Validate();

    [Theory]
    [InlineData(560, 380)]
    [InlineData(280, 360)]
    [InlineData(1920, 1080)]
    public void FitsAllRowsAndFooterWithoutChangingSavedPreferences(double width, double height)
    {
        var settings = new AppSettings { Scale = 2, PanelWidth = 900, PanelHeight = 1400, MetricOrder = AppSettings.MetricNames.Keys.ToArray() };
        var fitted = PanelLayout.Fit(settings, width, height);
        Assert.InRange(fitted.Width, 1, width);
        Assert.InRange(fitted.Height, 1, height);
        Assert.True(fitted.Width / fitted.Scale >= 300 - 1e-9);
        Assert.True(fitted.Height / fitted.Scale >= settings.GetMinimumHeight() - 1e-9);
        Assert.Equal(2, settings.Scale);
        Assert.Equal(900, settings.PanelWidth);
        Assert.Equal(1400, settings.PanelHeight);
    }

    [Fact]
    public void RestoresRequestedSizeWhenSpaceReturns()
    {
        var settings = new AppSettings();
        Assert.True(PanelLayout.Fit(settings, 300, 350).Scale < settings.Scale);
        Assert.Equal(new PanelLayout(360, 554, 1), PanelLayout.Fit(settings, 1920, 1080));
    }

    [Theory]
    [InlineData("R")]
    [InlineData("S")]
    public void ReservedHotkeysCannotReplaceVisibilityShortcut(string key) =>
        Assert.Throws<InvalidDataException>(() => new AppSettings { ToggleKey = key }.Validate());
}
