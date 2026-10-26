# Telemetry

A compact Windows system overlay. Charcoal, precise typography, useful numbers.

**Early release · Windows 11 x64 · MIT licensed**

Telemetry is a native Windows desktop application written in C# on .NET 10. It uses Windows APIs and the graphics driver already installed on the machine. The release bundles its .NET runtime: no monitoring app, kernel sensor driver, account, service registration or runtime installation is required.

## What works

- System CPU utilization, RAM, commit accounting and target-process CPU/memory.
- NVIDIA GPU utilization, temperature, power/limit, graphics clock, fan duty, VRAM and supported clock-limit reasons through the installed driver's NVML API.
- DirectX application present cadence through our own Windows ETW collector, with rolling FPS, 1% low and P99 intervals.
- Click-through overlay; drag and edge-resize in edit mode; per-monitor placement and Windows DPI scaling.
- Global shortcuts, tray controls, optional sign-in launch, editable colors/fonts, row ordering and recording exports.
- Bounded buffers, explicit unavailable readings and capture-loss reporting.

## Install

Download `Telemetry-Setup-0.2.0.exe` from [Releases](https://github.com/matt-wigg/telemetry-overlay/releases). It installs per user and includes an uninstaller. A portable ZIP is also available. These initial builds are unsigned; publisher signing is a future distribution milestone.

Launch **Telemetry Controls** from Start. The panel also has a tray icon. No extra sensor setup is needed for standard monitoring.

| Shortcut | Action |
| --- | --- |
| Ctrl+Alt+O | Show/hide the overlay (letter configurable) |
| Ctrl+Alt+E | Unlock/lock placement; drag edges to resize |
| Ctrl+Alt+R | Start/stop capture |
| Escape, while editing | Lock placement |

The widget contains measurements and a compact status line. All configuration stays in Controls. It passes clicks through while locked; use Controls → Move / resize or Ctrl+Alt+E to unlock it. Closing Controls keeps the tray app running; choose Quit in the tray to exit.

## FPS permission

Windows restricts cross-process event tracing. In Controls → Diagnostics, choose **Enable FPS…** and approve the Windows prompt. Only the collector is elevated; the overlay stays a normal-user process. Permission lasts for the running session. Telemetry does not alter group membership, turn off security features or install a service to avoid this prompt.

FPS is **successful application Present-call cadence**, not display FPS, frame-generated FPS or end-to-end latency. DXGI and Direct3D9 are supported paths. Some Vulkan/OpenGL applications may present through a compatible path; do not assume coverage. This is not a PresentMon replacement with equivalent display-pipeline analysis.

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
