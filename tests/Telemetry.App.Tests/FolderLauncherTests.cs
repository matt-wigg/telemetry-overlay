using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace Telemetry.App.Tests;

public sealed class FolderLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Telemetry folder test " + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FirstUseCreatesDirectoryAndPreservesPathWithSpaces()
    {
        ProcessStartInfo? request = null;
        string path = Path.Combine(_root, "Captures with spaces");
        Assert.Null(FolderLauncher.TryOpen(path, start => request = start));
        Assert.True(Directory.Exists(path));
        Assert.NotNull(request);
        Assert.Equal(path, request.FileName);
        Assert.Empty(request.ArgumentList);
        Assert.True(request.UseShellExecute);
    }

    [Fact]
    public void FilesystemFailureDoesNotLaunchOrEscapeCallback()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "Captures");
        File.WriteAllText(path, "Existing file must not be overwritten.");
        bool launched = false;
        Assert.NotNull(FolderLauncher.TryOpen(path, _ => launched = true));
        Assert.False(launched);
        Assert.Equal("Existing file must not be overwritten.", File.ReadAllText(path));
    }

    [Fact]
    public void ShellFailureReturnsReadableError()
    {
        string? error = FolderLauncher.TryOpen(_root, _ => throw new Win32Exception(2, "Folder handler unavailable."));
        Assert.Contains("Folder handler unavailable", error);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
