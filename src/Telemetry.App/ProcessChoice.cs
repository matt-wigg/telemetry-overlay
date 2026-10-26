using System.Diagnostics;

namespace Telemetry.App;

// A PID alone is not an identity: Windows can reuse it after a process exits.
internal sealed record ProcessChoice(int Pid, string Name, long StartedUtcTicks)
{
    internal string Label => $"{Name} · {Pid}";

    internal static ProcessChoice? Read(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return new(pid, process.ProcessName, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { return null; }
    }

    internal bool IsRunning => Read(Pid) is { } current && current.StartedUtcTicks == StartedUtcTicks;

    internal static ProcessChoice[] List(bool includeBackground)
    {
        var choices = new List<ProcessChoice>();
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.ProcessName == "Telemetry" || (!includeBackground && process.MainWindowHandle == 0)) continue;
                if (Read(process.Id) is { } choice) choices.Add(choice);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        return choices.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToArray();
    }
}
