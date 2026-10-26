using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace Telemetry.App;

internal static class FolderLauncher
{
    internal static string? OpenCaptures() => TryOpen(Path.Combine(SettingsStore.DirectoryPath, "Captures"),
        start => { using var process = Process.Start(start); });

    // Return recoverable shell/filesystem errors to the caller instead of allowing a
    // button or tray callback to terminate the application's dispatcher.
    internal static string? TryOpen(string path, Action<ProcessStartInfo> launch)
    {
        try
        {
            Directory.CreateDirectory(path);
            launch(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true, Verb = "open" });
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or SecurityException or ArgumentException or InvalidOperationException)
        {
            SettingsStore.Log(error);
            return "Couldn't open the captures folder. " + error.Message;
        }
    }
}
