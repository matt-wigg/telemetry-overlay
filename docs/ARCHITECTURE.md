# Architecture

## Technology decisions

| Concern | Choice | Reason |
| --- | --- | --- |
| Application | C# 14 / .NET 10 LTS | Memory safety, strong types, mature Windows integration; supported runtime |
| Overlay | WPF DrawingContext, frozen brushes and bounded vector trace | Direct custom typography/layout without browser runtime or HTML UI |
| Window behavior | Win32 P/Invoke | Hotkeys, non-activating click-through window, resize hit-testing, DPI/monitor placement |
| System metrics | GetSystemTimes, GlobalMemoryStatusEx, process counters | No separate tool or hardware driver |
| GPU metrics | Installed NVIDIA NVML driver API | GPU telemetry without shipping a vendor binary or invoking nvidia-smi |
| Frame metrics | StartTrace/EnableTraceEx2/OpenTrace/ProcessTrace | Own targeted ETW reader for DXGI/D3D9 Present calls |
| Isolation | Normal-user UI and optional elevated collector process | Sensor/tracing work off the UI thread; limited privilege surface |
| IPC | Local named pipe, current-user SID ACL | No listening TCP port or account service; supports normal↔elevated token transition |
| Configuration | Validated JSON, replace-with-backup writes | Human-editable, recoverable and versioned |
| Packaging | Self-contained win-x64 publish + Inno Setup | End users do not install a runtime; normal per-user uninstall |
| Tests and CI | xUnit; GitHub Actions Windows build | Deterministic measurement tests and release packaging |

Language alone does not establish speed or precision. The .NET UI has a real working-set cost; native ETW timing, sampling policy, rendering rate and correct definitions matter more than a blanket “C++ is faster” claim. Measure the whole process tree. If a measured hotspot remains, replace that specific provider or renderer behind its boundary.

## Modules

- `Telemetry.Core`: settings schema, metric DTOs, severity hysteresis and exact statistical calculations. No Windows dependency.
- `Telemetry.App`: overlay, controls, tray lifecycle, native integration and measurement providers.
- `CollectorHost`: the same executable in `--collector` mode, doing collection off-process. Only a generated local pipe name is accepted. No arbitrary executable path or output path is accepted by the helper.
- `CaptureWriter`: local sensor CSV, per-frame CSV and session summary. Captures lock the target process.

The UI sends a target PID and whether frame collection is needed. The collector reports sensor snapshots and batches of frame events. The renderer never queries hardware synchronously.

## Runtime and space complexity

Let F be frames in a live 10-second window, bounded by 32,768; C be swap chains, capped at 64; T be outstanding presenting threads, capped at 256.

| Operation | Time | Memory |
| --- | --- | --- |
| Event callback / frame append | Expected O(1) dictionaries and queue | Bounded O(C+T+16,384 pending frames) |
| Ring append | O(1) | Fixed 32,768-frame ring |
| Primary swap-chain selection | O(F) grouping | O(F) snapshot/groups |
| Exact live percentiles | O(F log F), twice per second | O(F) scratch |
| Overlay graph | O(60) | 60 samples |
| Sensor sampling | Fixed set, twice per second | Bounded |
| Capture append | O(frames in batch) | At most 1,000,000 durations across 32 swap chains |
| Final capture statistics | O(N log N) | O(N), bounded by capture limit |

Sorting exact bounded samples is intentional. A histogram would reduce cost but introduce approximation. Capture writing and finalization must be profiled; the initial implementation does not promise hard real-time scheduling or zero allocations.

## Frame timing

The ETW session requests QPC timestamps. Successful start/stop pairs are matched by thread, then intervals are calculated independently per swap chain. DXGI test presents and nonzero HRESULT results are excluded. The dominant swap chain in the current window is shown; swap chains are never pooled into a false combined FPS. ETW loss and local queue loss are reported.

This captures Present-call cadence, not photon output. GPU-busy, displayed-frame classification and generated FPS are intentionally unavailable. The named session is stable per interactive Windows session and stopped on normal disposal.

## Current platform scope

Windows 11 x64 is the release target. Native ETW structure layouts and pointer-width handling require regression checks when adding ARM64 or other platforms. The self-contained runtime includes Microsoft framework components; “self-contained” does not mean the application implements the operating system or GPU driver itself.
