# Telemetry: implementation and release plan

## Product

A movable, resizable system overlay with compact statistics, stable numeric typography, square geometry, muted semantic colors and a coherent Controls window. The same product serves everyday inspection and bounded benchmark recording.

The application is open source under MIT. Source, tests, default configuration, installer scripts and documentation are public. Personal recordings, diagnostic logs, credentials and local configuration remain local.

## Supported first release

1. Native Windows 11 x64 application, optional sign-in startup, tray lifecycle and a single running UI instance.
2. Configurable show/hide shortcut; dedicated edit, Controls and record shortcuts. Conflicts are surfaced, with tray actions available.
3. Automatic foreground-monitor selection, pointer-monitor mode and pinned-monitor mode. Persist placements per display; clamp recovery to visible bounds.
4. Locked click-through state and explicit edit state with drag/resize. Logical dimensions and interface scale are separate controls.
5. Windows system/process metrics; NVIDIA telemetry through the installed driver. No additional monitoring tools or sensor-driver installation.
6. Own targeted DXGI/D3D9 ETW collector. FPS permission is optional and explicitly user-approved through Windows.
7. Defined live statistics, bounded recording, CSV and JSON export, and capture-loss indication.
8. A self-contained portable build plus per-user installer/uninstaller. Controls has General, Appearance, Metrics, Capture and Diagnostics pages. The widget has only compact shortcut keycaps beside its status, with no configuration fields.

## Engineering choices

C# 14, .NET 10 LTS, WPF custom drawing, native Win32 and ETW APIs, installed NVML driver API, current-user-restricted named pipes, JSON configuration, xUnit tests, GitHub Actions and Inno Setup. See ARCHITECTURE.md for complexity and tradeoffs. There are no third-party runtime NuGet dependencies in the revised implementation.

Exact percentile calculations operate on bounded windows, O(F log F). Event append is O(1), graph history is fixed-size, and hardware sampling is separate from per-frame measurement. “Perfect code” is not an attainable verification claim: tests, profiling, review and compatibility evidence are release criteria.

## Visual specification

- #181818 panel, #F0F0F0 primary text, #A3A3A3 secondary text.
- Monospace numeric content with right-aligned values; system font fallback if the preferred font is absent.
- Square frame and thin rules; no decorative glow or animated background.
- One prominent FPS reading, frame consistency immediately underneath, then ordered hardware/process rows.
- Muted teal for busy activity; amber/red for persistent configured thresholds with hysteresis.
- A small named capability/status footer. Missing data is explicit.
- Controls uses the same dark palette and restrained typography, with persistent actions and an Apply button.

## Versioning and distribution

The canonical checkout is a normal Git repository, separate from the installed application. `main` is the integration branch; feature branches and pull requests hold changes. Every release gets a SemVer tag, release notes, installer, portable ZIP and SHA-256 checksums. Lockfiles and a pinned SDK with patch roll-forward make dependency changes reviewable. GitHub backs up committed/pushed code; it does not back up uncommitted files or local captures. Required CI checks cover Windows builds, regression tests, CodeQL and dependency review.

## Verification gates

- Build with nullable analysis and warnings treated as errors.
- Mathematical tests for statistics and hysteresis; edge cases for missing data and bounds.
- Standard-user launch and startup recovery.
- Native resize/drag, hotkeys, tray and two-monitor placement checks.
- Live GPU/system readings compared to independent OS/driver evidence.
- ETW permission, denied/canceled permission recovery, successful capture and trace cleanup.
- Portable and installed builds both launch; installer/uninstaller preserve user data.
- Whole-process CPU/memory sampling; later paired overlay-on/off game benchmarks.
- Before a stable 1.0 claim: extended uptime, sleep/resume, mixed-DPI disconnects, representative games, third-party review and signing/distribution review.

## Future capabilities, in priority order

1. More game and display-mode verification, process-selection edge cases and ETW-loss accounting.
2. Export analysis/comparison UI and named benchmark profiles.
3. AMD/Intel telemetry and explicit adapter selection.
4. Optional richer frame-pipeline metrics only when measurement accuracy can be established.
5. Signed releases, verified opt-in update mechanism, accessibility expansion and localization.

CPU package temperature would require a separate supported hardware-access strategy. It is not promised in the driver-free scope. Exclusive-fullscreen injection is not part of this release.
