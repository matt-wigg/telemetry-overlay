using System.Diagnostics;
using Xunit;

namespace Telemetry.App.Tests;

public sealed class ProcessChoiceTests
{
    [Fact]
    public void IdentityRejectsReusedPidAndMissingProcess()
    {
        var current = ProcessChoice.Read(Environment.ProcessId);
        Assert.NotNull(current);
        Assert.True(current.IsRunning);
        Assert.False((current with { StartedUtcTicks = current.StartedUtcTicks - 1 }).IsRunning);
        Assert.Null(ProcessChoice.Read(int.MaxValue));
    }

    [Fact]
    public void IdentityDoesNotFollowAnExitedProcess()
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c pause")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true })!;
        var target = ProcessChoice.Read(process.Id);
        Assert.NotNull(target);
        Assert.True(target.IsRunning);
        process.StandardInput.WriteLine();
        Assert.True(process.WaitForExit(5000));
        Assert.False(target.IsRunning);
    }
}
