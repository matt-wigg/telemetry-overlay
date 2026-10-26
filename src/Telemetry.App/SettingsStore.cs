using System.Text.Json;
using Microsoft.Win32;

namespace Telemetry.App;

internal static class SettingsStore
{
    internal static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Telemetry");
    internal static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal static string? LoadWarning { get; private set; }
    private static bool _loadFailed;

    internal static AppSettings Load()
    {
        LoadWarning = null; _loadFailed = false;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!File.Exists(FilePath)) return new() { StartAtSignIn = IsStartupEnabled() };
            if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException("Settings file is too large.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.Validate();
            settings.StartAtSignIn = IsStartupEnabled();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or ArgumentException or NullReferenceException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _loadFailed = true;
            LoadWarning = "Settings could not be loaded; defaults are active. The original file is preserved.";
            Log(e);
            return new() { StartAtSignIn = IsStartupEnabled() };
        }
    }

    internal static void Save(AppSettings settings, bool allowRecovery = false)
    {
        settings.Validate();
        if (_loadFailed && !allowRecovery) return;
        Directory.CreateDirectory(DirectoryPath);
        if (_loadFailed && File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".recovery-" + Guid.NewGuid().ToString("N"));
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
        else File.Move(temporary, FilePath);
        _loadFailed = false;
    }

    internal static void SetStartup(bool enabled)
    {
        RestoreStartupCommand(enabled ? $"\"{Environment.ProcessPath}\" --startup" : null);
    }
    internal static string? GetStartupCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Telemetry") as string;
    }
    internal static void RestoreStartupCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (command is not null) key.SetValue("Telemetry", command);
        else key.DeleteValue("Telemetry", false);
    }
    private static bool IsStartupEnabled()
    {
        try { return GetStartupCommand() is string command && command.StartsWith($"\"{Environment.ProcessPath}\"", StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { Log(e); return false; }
    }

    internal static void Log(Exception error)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, "diagnostics.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {error.GetType().Name}: {error.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
