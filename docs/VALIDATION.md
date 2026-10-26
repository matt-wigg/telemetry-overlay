# Verification record

Version 0.2 is a preview, not universal hardware/game certification. Raw evidence stays in ignored local `work/` directories because it includes process names and machine information.

## Review and checks

- Release compilation treats warnings as errors. The regression suite has 31 passing tests covering interval statistics, Present pairing, invalid samples, bounded IPC, delayed graph delivery, configuration validation, recording boundaries, capture loss and Windows x64 ETW structure layouts.
- Native smoke checks verify HWND visibility, show/hide, edit-mode input, resizing, remembered placement, click-through/no-activate styles, tray visibility and registration of all three shortcuts.
- Application-rendered Controls and overlay images were inspected for typography, spacing and alignment. Metric columns now have disjoint width limits at the minimum panel size. The settings dropdown indicator has explicit dark-theme contrast.
- A desktop capture exposed a blank hardware-rendered Controls surface despite working input. Software rendering corrected it in the same session. Compact metric labels remain readable at the minimum width. Actual Ctrl+Alt+O and Ctrl+Alt+E input was verified in the desktop session.
- The 2026-10-26 D3D11 retest captured and matched all 263 intervals from 264 successful test Presents. Final trace drain was acknowledged, with zero reported loss; mean absolute interval difference was 0.00195 ms and maximum 0.17270 ms. This timer-paced workload verifies measurement behavior, not game performance.
- Recording completion requires an acknowledged ETW buffer drain, at least two usable intervals on the primary swap chain, and no reported event/sample loss or capacity limit. A forced shutdown or failed drain produces an incomplete summary.
- NVIDIA memory accounting uses the versioned driver API to separate reserved memory. Windows RAM totals were compared with OS reporting. Dynamic readings are sequential samples, not simultaneous observations.
- Earlier local installer checks confirmed per-user installation, executable hash agreement, startup registration and uninstall preservation of settings. Repeat these for each changed installer.
- The 0.2 package installation preserved settings; all 485 installed payload files matched the portable package. GitHub's Windows build/test workflow and CodeQL analysis passed, with no open CodeQL or secret-scanning alerts at review time. Dependency review also ran successfully on update pull requests.

## Repeatable checks

Run `build/Verify.ps1` on a Windows desktop after quitting Telemetry. Reports and PNG renders are saved under ignored `work/`. Add `-IncludeFps` to run a finite D3D11 workload and compare ETW intervals to independently recorded Present-call timestamps, including the final buffer drain. This requires a user-approved Windows permission prompt. A denied or pending prompt is not a passing result.

`Telemetry.exe --quit` requests clean shutdown. A second launch opens the existing Controls window. Verification refuses to interrupt an already-running UI session.

CI runs unit/integration tests and packaging; it does not claim to exercise interactive monitor placement or administrator-approved live tracing.

## Performance scope and remaining gates

WPF/.NET has a meaningful runtime and memory cost. Live exact percentiles sort a bounded window twice per second; no paired game benchmark establishes the overlay's impact on game FPS. Do not describe this build as an ultra-small binary or as zero overhead.

- Real-game coverage across DX11/DX12, D3D9, Vulkan/OpenGL paths, anti-cheat software and multiple swap chains.
- Long recordings, extended uptime, sleep/resume, driver resets, hot-unplug and mixed-DPI monitor transitions.
- Physical shortcut input and tray recovery after Explorer restarts; registration checks are narrower evidence.
- Broader accessibility evaluation, independent security review and publisher signing before a stable 1.0 release.
- The implemented design is the vertical instrument panel. A horizontal ribbon remains a future layout.
- CPU package temperature, AMD/Intel GPU telemetry, displayed/generated frame classification and display latency are not implemented. Unavailable values remain unavailable.
