using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Telemetry.App;

/// <summary>Native ETW DirectX present-call tracing. No injection, third-party executable or kernel driver.</summary>
internal sealed unsafe class FrameProvider : IDisposable
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private readonly string _session = "Telemetry-DirectX-Session" + Process.GetCurrentProcess().SessionId;
    private readonly object _gate = new();
    private readonly Queue<FrameSample> _pending = new(16384);
    private readonly PresentAccumulator _accumulator = new(Stopwatch.Frequency);
    private readonly EventCallback _callback;
    private ulong _trace, _consumer = ulong.MaxValue;
    private nint _properties, _loggerName;
    private Task<uint>? _pump;
    private int _target;
    private string _status = "Choose a DirectX application";
    private long _dropped;
    private long _completedSessionLoss;
    private bool _attempted, _disposed;

    internal FrameProvider() => _callback = OnEvent;

    internal void Target(int pid)
    {
        if (_disposed || pid == _target) return;
        lock (_gate) { _target = pid; _accumulator.Clear(); _pending.Clear(); }
        if (pid <= 0) { Stop(); _attempted = false; _status = "FPS paused"; return; }
        if (!_attempted) Start();
        if (_trace != 0)
        {
            uint process = (uint)pid;
            var filter = new FilterDescriptor { Pointer = (ulong)&process, Size = 4, Type = 0x80000004 };
            var parameters = new EnableParameters { Version = 2, Filter = (nint)(&filter), FilterCount = 1 };
            var dxgi = Dxgi; var d3d = D3d9;
            uint a = EnableTraceEx2(_trace, ref dxgi, 1, 0, 0x2, 0, 0, ref parameters);
            uint b = EnableTraceEx2(_trace, ref d3d, 1, 0, 0x2, 0, 0, ref parameters);
            _status = a == 0 || b == 0 ? "DirectX trace · waiting for presents" : $"DirectX provider error {a}/{b}";
        }
    }

    private void Start()
    {
        _attempted = true;
        int structureSize = Marshal.SizeOf<TraceProperties>();
        int bytes = structureSize + (_session.Length + 1) * 2;
        _properties = Marshal.AllocHGlobal(bytes);
        new Span<byte>((void*)_properties, bytes).Clear();
        var properties = new TraceProperties
        {
            BufferBytes = (uint)bytes,
            ClientContext = 1,
            WnodeFlags = 0x20000,
            BufferSize = 64,
            MinimumBuffers = 4,
            MaximumBuffers = 16,
            LogFileMode = 0x100,
            FlushTimer = 1,
            LoggerNameOffset = (uint)structureSize
        };
        Marshal.StructureToPtr(properties, _properties, false);
        uint error = StartTraceW(out _trace, _session, _properties);
        if (error == 183)
        {
            // One stable, per-interactive-session name; reclaim only our own orphaned session.
            ControlTraceW(0, _session, _properties, 1);
            Marshal.StructureToPtr(properties, _properties, false);
            error = StartTraceW(out _trace, _session, _properties);
        }
        if (error != 0)
        {
            _trace = 0;
            _status = error == 5 ? "FPS permission required · enable in Controls" : $"Windows trace error {error}";
            Marshal.FreeHGlobal(_properties); _properties = 0;
            return;
        }
        _loggerName = Marshal.StringToHGlobalUni(_session);
        var logfile = new TraceLogfile
        {
            LoggerName = _loggerName,
            ProcessTraceMode = 0x100 | 0x10000000 | 0x1000,
            EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback)
        };
        _consumer = OpenTraceW(ref logfile);
        if (_consumer == ulong.MaxValue) { _status = "Windows trace consumer could not open"; Stop(); return; }
        ulong consumer = _consumer;
        _pump = Task.Factory.StartNew(() =>
        {
            var handles = new[] { consumer };
            uint result = ProcessTrace(handles, 1, 0, 0);
            GC.KeepAlive(_callback);
            return result;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void OnEvent(ref EventRecord record)
    {
        if (record.Header.ProcessId != (uint)Volatile.Read(ref _target)) return;
        var header = record.Header;
        bool dxgi = header.ProviderId == Dxgi;
        bool d3d9 = header.ProviderId == D3d9;
        if (!dxgi && !d3d9) return;
        bool start = dxgi ? header.Id is 42 or 55 : header.Id == 1;
        bool stop = dxgi ? header.Id is 43 or 56 : header.Id == 2;
        if (!start && !stop) return;
        lock (_gate)
        {
            if (header.ProcessId != (uint)_target) return;
            if (start)
            {
                int pointerSize = (header.Flags & 0x20) != 0 ? 4 : 8;
                if (record.UserDataLength < pointerSize + (dxgi ? 4 : 0)) return;
                // DXGI's first fields are pIDXGISwapChain and Flags. Ignore DXGI_PRESENT_TEST.
                bool test = dxgi && (Marshal.ReadInt32(record.UserData, pointerSize) & 1) != 0;
                ulong chain = pointerSize == 8 ? (ulong)Marshal.ReadInt64(record.UserData) : (uint)Marshal.ReadInt32(record.UserData);
                _accumulator.Start(dxgi, header.ThreadId, chain, header.Timestamp, test);
                return;
            }
            if (record.UserDataLength < 4) return;
            if (_accumulator.Stop(dxgi, header.ThreadId, (int)header.ProcessId, Marshal.ReadInt32(record.UserData)) is { } sample)
            {
                if (_pending.Count >= 16384) { _pending.Dequeue(); _dropped++; }
                _pending.Enqueue(sample);
                _status = "DirectX · application present cadence";
            }
        }
    }

    internal (FrameSample[] Frames, string Status, long Dropped) Drain()
    {
        long lost = 0;
        if (_trace != 0 && _properties != 0 && ControlTraceW(_trace, null, _properties, 0) == 0)
        {
            var properties = Marshal.PtrToStructure<TraceProperties>(_properties);
            lost = properties.EventsLost;
        }
        lock (_gate) { var frames = _pending.ToArray(); _pending.Clear(); return (frames, _status, _dropped + _completedSessionLoss + lost + _accumulator.RejectedForCapacity); }
    }
    internal bool FinishCapture()
    {
        bool drained = Stop();
        lock (_gate) { _target = 0; _accumulator.Clear(); }
        _attempted = false;
        return drained;
    }
    private bool Stop()
    {
        bool stopped = _trace != 0 && ControlTraceW(_trace, null, _properties, 1) == 0;
        if (stopped && _properties != 0) _completedSessionLoss += Marshal.PtrToStructure<TraceProperties>(_properties).EventsLost;
        _trace = 0;
        // Stopping the session flushes its buffers. Let ProcessTrace consume them before closing its handle.
        bool finished = _pump is null || _pump.Wait(TimeSpan.FromSeconds(5));
        bool drained = stopped && finished && _pump?.Result == 0;
        if (_consumer != ulong.MaxValue) { CloseTrace(_consumer); _consumer = ulong.MaxValue; }
        var pump = _pump; _pump = null;
        nint logger = _loggerName, properties = _properties;
        _loggerName = 0; _properties = 0;
        void FreeNativeMemory()
        {
            if (logger != 0) Marshal.FreeHGlobal(logger);
            if (properties != 0) Marshal.FreeHGlobal(properties);
        }
        if (finished) FreeNativeMemory();
        else _ = pump!.ContinueWith(_ => FreeNativeMemory(), TaskScheduler.Default);
        return drained;
    }
    public void Dispose() { _disposed = true; Stop(); GC.KeepAlive(_callback); }

    [StructLayout(LayoutKind.Sequential)]
    private struct TraceProperties
    {
        public uint BufferBytes, ProviderId; public ulong HistoricalContext; public long TimeStamp; public Guid Guid;
        public uint ClientContext, WnodeFlags, BufferSize, MinimumBuffers, MaximumBuffers, MaximumFileSize, LogFileMode, FlushTimer, EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers, FreeBuffers, EventsLost, BuffersWritten, LogBuffersLost, RealTimeBuffersLost;
        public nint LoggerThreadId; public uint LogFileNameOffset, LoggerNameOffset;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FilterDescriptor { public ulong Pointer; public uint Size, Type; }
    [StructLayout(LayoutKind.Sequential)] private struct EnableParameters { public uint Version, EnableProperty, ControlFlags; public Guid SourceId; public nint Filter; public uint FilterCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TraceLogfile
    {
        public nint LogFileName, LoggerName; public long CurrentTime; public uint BuffersRead, ProcessTraceMode;
        public fixed byte CurrentEventAndHeader[368];
        public nint BufferCallback; public uint BufferSize, Filled, EventsLost;
        public nint EventRecordCallback; public uint IsKernelTrace; public nint Context;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EventHeader
    {
        public ushort Size, HeaderType, Flags, EventProperty;
        public uint ThreadId, ProcessId; public long Timestamp; public Guid ProviderId;
        public ushort Id; public byte Version, Channel, Level, Opcode; public ushort Task; public ulong Keyword, ProcessorTime; public Guid ActivityId;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct EventRecord
    {
        public EventHeader Header; public uint BufferContext; public ushort ExtendedDataCount, UserDataLength;
        public nint ExtendedData, UserData, UserContext;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void EventCallback(ref EventRecord record);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint StartTraceW(out ulong session, string name, nint properties);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern uint ControlTraceW(ulong session, string? name, nint properties, uint code);
    [DllImport("advapi32.dll")] private static extern uint EnableTraceEx2(ulong session, ref Guid provider, uint code, byte level, ulong anyKeyword, ulong allKeyword, uint timeout, ref EnableParameters parameters);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern ulong OpenTraceW(ref TraceLogfile logfile);
    [DllImport("advapi32.dll")] private static extern uint ProcessTrace([In] ulong[] handles, uint count, nint start, nint end);
    [DllImport("advapi32.dll")] private static extern uint CloseTrace(ulong handle);
}
