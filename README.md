# Telemetry

A compact Windows system overlay. Charcoal, precise typography, useful numbers.

**Early release · Windows 11 x64 · MIT licensed**

Telemetry is a native Windows desktop application written in C# on .NET 10. It uses Windows APIs and the graphics driver already installed on the machine. The release bundles its .NET runtime: no monitoring app, kernel sensor driver, account, service registration or runtime installation is required.

## What works

- System CPU utilization, RAM, commit accounting and target-process CPU/memory.
- NVIDIA GPU utilization, temperature, power/limit, graphics clock, fan duty, VRAM and supported clock-limit reasons through the installed driver's NVML API.
- DirectX application present cadence through our own Windows ETW collector: rolling FPS, frame time and Low FPS in the widget; P99 and exact statistics in exports.
- Follow-active-app mode or a free desktop widget with an application picker; drag and edge-resize in edit mode, per-monitor placement and Windows DPI scaling.
- Global shortcuts, tray controls, optional sign-in launch, editable colors/fonts, row ordering and recording exports.
- Bounded buffers, explicit unavailable readings and capture-loss reporting.

## Install

Download `Telemetry-Setup-0.3.0.exe` from [Releases](https://github.com/matt-wigg/telemetry-overlay/releases). It installs per user and includes an uninstaller. A portable ZIP is also available. These initial builds are unsigned; publisher signing is a future distribution milestone.

Launch **Telemetry Controls** from Start. The panel also has a tray icon. No extra sensor setup is needed for standard monitoring.

| Shortcut | Action |
| --- | --- |
| Ctrl+Alt+O | Show/hide the overlay (letter configurable) |
| Ctrl+Alt+E | Unlock/lock placement; drag edges to resize |
| Ctrl+Alt+S | Open Controls |
| Ctrl+Alt+R | Start/stop capture |
| Escape, while editing | Lock placement |

The widget contains measurements, an optional application picker and a compact status strip with shortcut keycaps. Configuration stays in Controls. Use Controls → Move / resize or Ctrl+Alt+E to unlock placement. While editing it is also available in Alt+Tab. Closing Controls keeps the tray app running; choose Quit in the tray to exit. On a smaller desktop the widget temporarily fits itself to the available space without overwriting your preferred dimensions.

## Choose what to follow

In Controls → General, choose a **Widget mode**:

- **Follow active app** tracks the foreground application's display and snaps to your selected display corner. The locked panel passes mouse input through to the game.
- **Free placement** stays on a fixed display while other applications gain focus. Choose a running application from the header; its FPS and App CPU/RAM readings stay selected. The header is interactive in this mode, so the widget receives mouse clicks. Unlock placement to move it across displays or resize it; locking retains its new display and position.

The picker separates application names from process IDs and supports typing a name. Enable **Include background processes** to list accessible processes without a main window. Services usually have CPU/memory readings but no graphics Presents, so FPS can be unavailable. System CPU, GPU, RAM and other hardware rows always describe the machine, not exclusive resource use by the selected app.

Selections last for the current Telemetry session. An exited process is not silently replaced with another process reusing its ID. Select a restarted game again. The target is locked while recording. If it exits, the capture is finalized as incomplete rather than claiming a confirmed final drain.

## FPS permission

Windows restricts cross-process event tracing. In Controls → Diagnostics, choose **Enable FPS…** and approve the Windows prompt. That one approval covers all applications for the running collector session; switching games does not ask again. Only the collector is elevated; the overlay stays a normal-user process. Exiting Telemetry, restarting its collector or rebooting ends the session. Telemetry does not alter group membership, turn off security features or install a service to avoid this prompt.

FPS is **successful application Present-call cadence**, not display FPS, frame-generated FPS or end-to-end latency. DXGI and Direct3D9 are supported paths. Some Vulkan/OpenGL applications may present through a compatible path; do not assume coverage. This is not a PresentMon replacement with equivalent display-pipeline analysis.

An FPS em dash means **unavailable**, not zero. A replacement presentation surface can take over once it has two intervals and recent data; retired surfaces do not suppress its reading. Genuine pauses or trace-delivery gaps still expire after 2.5 seconds. The caption distinguishes collecting/waiting from no recent frames. Diagnostics shows frame age, collector-packet age and the selected presentation surface.

## Deliberate capability limits

- Windows has no universal CPU-temperature API. CPU package temperature, CPU power and effective clocks are unavailable in this driver-free release. ACPI thermal-zone temperatures are not substituted.
- Detailed GPU readings currently support the first NVIDIA adapter exposed by NVML. AMD/Intel vendor telemetry and explicit multi-GPU selection are future adapters. Unsupported sensors display an em dash.
- Desktop and borderless windows are the intended overlay modes. Exclusive fullscreen and anti-cheat compatibility are not guaranteed. There is no game injection.
- No automatic updater, cloud telemetry, fan control or overclocking.
- An initial working release is not a claim of universal game validation or production certification. See [validation](docs/VALIDATION.md).

## Build

Prerequisite: .NET 10 SDK on Windows x64. For an installer, also install Inno Setup 6.

```powershell
git clone https://github.com/matt-wigg/telemetry-overlay.git
cd telemetry-overlay
dotnet restore tests/Telemetry.Core.Tests/Telemetry.Core.Tests.csproj --locked-mode
dotnet test tests/Telemetry.Core.Tests/Telemetry.Core.Tests.csproj -c Release --no-restore
dotnet restore tests/Telemetry.App.Tests/Telemetry.App.Tests.csproj --locked-mode
dotnet test tests/Telemetry.App.Tests/Telemetry.App.Tests.csproj -c Release --no-restore
dotnet build src/Telemetry.App/Telemetry.App.csproj -c Release
./build/Publish.ps1
```

The app has **zero third-party runtime NuGet packages**. xUnit is used only by the test project. Native dependencies are Windows system DLLs and optional `nvml.dll` loaded only from System32.

## Files and privacy

Settings, bounded local diagnostics and captures live under `%LOCALAPPDATA%\Telemetry`. Captures contain process names/IDs, timing and hardware measurements; inspect before sharing. The application makes no network requests. User settings and captures are not committed to this repository.

- [Architecture, technologies and complexity](docs/ARCHITECTURE.md)
- [Full implementation and release plan](docs/PLAN.md)
- [Metrics and color semantics](docs/METRICS.md)
- [Verification status](docs/VALIDATION.md)
- [Maintenance and release procedure](docs/MAINTENANCE.md)
- [Security model](SECURITY.md)

Contributions should include a reproducible test or trace when changing measurement behavior. See [CONTRIBUTING.md](CONTRIBUTING.md).

GitHub Actions runs locked restores, regression tests, self-contained packaging and CodeQL analysis. Pull requests also receive dependency review. See [repository security](docs/REPOSITORY-SECURITY.md) for contribution and release controls.
