using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Telemetry.App;

/// <summary>OS and installed graphics-driver APIs only. No extra sensor drivers or tools.</summary>
internal sealed class SensorProvider : IDisposable
{
    private ulong _idle, _kernel, _user;
    private readonly string _cpuName;
    private readonly NvidiaProvider _gpu = new();
    private int _pid;
    private TimeSpan _processCpu;
    private long _processSample;
    internal SensorProvider()
    {
        Native.GetSystemTimes(out _idle, out _kernel, out _user);
        using var cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        _cpuName = (cpu?.GetValue("ProcessorNameString") as string)?.Trim() ?? "CPU";
    }
    internal SensorSnapshot Read(int targetPid = 0)
    {
        double? cpuLoad = null, processLoad = null, processMemory = null;
        if (Native.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel - _kernel + user - _user;
            if (total > 0) cpuLoad = Math.Clamp(100.0 * (total - Math.Min(total, idle - _idle)) / total, 0, 100);
            (_idle, _kernel, _user) = (idle, kernel, user);
        }
        try
        {
            if (targetPid > 0)
            {
                using var process = Process.GetProcessById(targetPid);
                long now = Stopwatch.GetTimestamp();
                var time = process.TotalProcessorTime;
                if (_pid == targetPid && _processSample > 0)
                    processLoad = Math.Clamp((time - _processCpu).TotalSeconds / Stopwatch.GetElapsedTime(_processSample, now).TotalSeconds / Environment.ProcessorCount * 100, 0, 100);
                _pid = targetPid; _processCpu = time; _processSample = now;
                processMemory = process.WorkingSet64 / 1073741824.0;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { _pid = 0; }
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        bool valid = Native.GlobalMemoryStatusEx(ref memory);
        var performance = new Native.PerformanceInformation { Size = (uint)Marshal.SizeOf<Native.PerformanceInformation>() };
        bool commitValid = Native.GetPerformanceInfo(ref performance, performance.Size);
        return _gpu.Read() with
        {
            CpuName = _cpuName,
            CpuLoad = cpuLoad,
            CpuTemperature = null,
            RamUsedGb = valid ? (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0 : null,
            RamTotalGb = valid ? memory.TotalPhysical / 1073741824.0 : null,
            CommitUsedGb = commitValid ? performance.CommitTotal * (double)performance.PageSize / 1073741824.0 : null,
            CommitTotalGb = commitValid ? performance.CommitLimit * (double)performance.PageSize / 1073741824.0 : null,
            ProcessId = targetPid,
            ProcessCpu = processLoad,
            ProcessRamGb = processMemory,
            SensorStatus = _gpu.Available ? "Windows + NVIDIA driver; CPU temperature not exposed by Windows" : "Windows metrics; GPU telemetry unavailable on this driver"
        };
    }
    public void Dispose() => _gpu.Dispose();
}

internal sealed class NvidiaProvider : IDisposable
{
    private nint _device;
    private bool _initialized;
    private bool _memoryV2Supported = true;
    internal bool Available { get; private set; }
    private string _name = "GPU";
    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu, Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfo { public ulong Total, Free, Used; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryInfoV2 { public uint Version; public ulong Total, Reserved, Free, Used; }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlInit_v2();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlShutdown();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out nint device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(nint device, StringBuilder name, uint length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetUtilizationRates(nint device, out Utilization rates);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetMemoryInfo(nint device, out MemoryInfo memory);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetMemoryInfo_v2(nint device, ref MemoryInfoV2 memory);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetTemperature(nint device, uint sensor, out uint temperature);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetTemperatureThreshold(nint device, uint threshold, out uint temperature);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetPowerUsage(nint device, out uint power);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetPowerManagementLimit(nint device, out uint power);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetClockInfo(nint device, uint clock, out uint mhz);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetFanSpeed(nint device, out uint speed);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)][DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int nvmlDeviceGetCurrentClocksThrottleReasons(nint device, out ulong reasons);
    internal NvidiaProvider()
    {
        try
        {
            if (nvmlInit_v2() != 0) return;
            _initialized = true;
            Available = nvmlDeviceGetHandleByIndex_v2(0, out _device) == 0;
            var text = new StringBuilder(128);
            if (Available && nvmlDeviceGetName(_device, text, 128) == 0) _name = text.ToString();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException) { Available = false; }
    }
    internal SensorSnapshot Read()
    {
        try { return ReadAvailable(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Available = false; return new(); }
    }
    private SensorSnapshot ReadAvailable()
    {
        if (!Available) return new();
        double? load = nvmlDeviceGetUtilizationRates(_device, out var use) == 0 ? use.Gpu : null;
        bool memoryValid = nvmlDeviceGetMemoryInfo(_device, out var mem) == 0;
        double? reservedGb = null;
        if (_memoryV2Supported)
        {
            var memoryV2 = new MemoryInfoV2 { Version = (uint)Marshal.SizeOf<MemoryInfoV2>() | (2u << 24) };
            try
            {
                if (nvmlDeviceGetMemoryInfo_v2(_device, ref memoryV2) == 0)
                {
                    memoryValid = true;
                    mem = new MemoryInfo { Total = memoryV2.Total, Free = memoryV2.Free, Used = memoryV2.Used };
                    reservedGb = memoryV2.Reserved / 1073741824.0;
                }
            }
            catch (EntryPointNotFoundException) { _memoryV2Supported = false; }
        }
        double? temperature = nvmlDeviceGetTemperature(_device, 0, out var temp) == 0 ? temp : null;
        double? power = nvmlDeviceGetPowerUsage(_device, out var watts) == 0 ? watts / 1000.0 : null;
        double? limit = nvmlDeviceGetPowerManagementLimit(_device, out var powerLimit) == 0 ? powerLimit / 1000.0 : null;
        double? clock = nvmlDeviceGetClockInfo(_device, 0, out var mhz) == 0 ? mhz : null;
        double? fan = nvmlDeviceGetFanSpeed(_device, out var speed) == 0 ? speed : null;
        double? thermalLimit = nvmlDeviceGetTemperatureThreshold(_device, 1, out var thermal) == 0 ? thermal : null;
        string? reason = null;
        if (nvmlDeviceGetCurrentClocksThrottleReasons(_device, out var flags) == 0)
            reason = (flags & 0x60) != 0 ? "THERMAL" : (flags & 0x80) != 0 ? "POWER BRAKE" : (flags & 0x4) != 0 ? "POWER CAP" : null;
        return new()
        {
            GpuName = _name,
            GpuLoad = load,
            GpuTemperature = temperature,
            GpuPower = power,
            GpuPowerLimit = limit,
            GpuClock = clock,
            GpuFan = fan,
            GpuThermalLimit = thermalLimit,
            GpuLimitReason = reason,
            VramUsedGb = memoryValid ? mem.Used / 1073741824.0 : null,
            VramTotalGb = memoryValid ? mem.Total / 1073741824.0 : null,
            VramReservedGb = reservedGb
        };
    }
    public void Dispose() { if (_initialized) { nvmlShutdown(); _initialized = false; Available = false; } }
}
