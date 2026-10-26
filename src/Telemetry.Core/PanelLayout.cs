namespace Telemetry.Core;

/// <summary>Fits the widget to the available desktop without changing the user's saved size.</summary>
public readonly record struct PanelLayout(double Width, double Height, double Scale)
{
    public static PanelLayout Fit(AppSettings settings, double availableWidth, double availableHeight)
    {
        double width = Math.Max(1, availableWidth), height = Math.Max(1, availableHeight);
        double scale = Math.Min(settings.Scale, Math.Min(width / 300, height / settings.GetMinimumHeight()));
        return new(Math.Min(settings.PanelWidth * scale, width), Math.Min(settings.PanelHeight * scale, height), scale);
    }
}
