# Verification record

## 0.3.1 frame delivery investigation

A background-only, 45-second observation of a continuously presenting DirectX game reproduced the 0.3.0 failure: two of 80 readouts after the five-second warmup were unavailable, with a maximum frame age of 2,556 ms and no reported event loss. Repeating the same observation with explicit buffer flushing produced zero unavailable readouts in 80 observations, a maximum frame age of 1,422 ms and no reported loss. These are sequential delivery checks on one machine, not a game-performance comparison or universal timing guarantee. Raw event data remains local under ignored `work/`.

All 52 automated tests pass, including stale-data expiry, late delivery, swap-chain selection, offscreen layout rendering and a missing-target diagnostic regression. After the game restarted, the full production collector/IPC observation passed for 60 seconds: zero blank readouts after the five-second warmup, maximum frame age 1,733 ms, maximum packet age 483 ms and no reported loss. The final caption change was checked with offscreen fixtures. No screen capture or interactive window tests were performed for this patch.

To check the full production collector and IPC path without opening diagnostic windows, quit Telemetry and run `Telemetry.exe --verify-live-fps <process-id> <report-directory>` while the selected DirectX application continuously presents. Approve the collector's Windows prompt. The command observes for 60 seconds and writes `live-fps-validation.json`, including frame age, packet age, FPS availability and reported loss. A five-second warmup is excluded. A real pause, application exit or denied approval does not pass this continuity check. No screenshots or keyboard/mouse automation are used. Restart Telemetry after the diagnostic finishes.

## 0.3 release checks

The 51-test suite passes. It covers interval statistics, Present pairing, bounded delivery/capture, small-desktop fitting, process identity, shell failures and rendering. New regressions verify that a retired presentation surface cannot suppress a fresh replacement, equal-count surface selection is stable, genuine stale samples expire, and a replacement needs at least two intervals.

Five synthetic WPF fixtures cover live, waiting and stale states, minimum/default width and doubled text scale. The widget renders offscreen with no native window handle. The final FPS design was reviewed from these fixture images; it was not tested using screen-control automation. Its background graph preserves missing-data gaps, and the readout distinguishes unavailable FPS from zero.

An earlier 0.3 candidate passed 26 native checks on two connected displays: 14 general window/tray/hotkey checks, five placement/resize/visibility checks per display, and two picker caption/input-routing checks. Physical mouse and keyboard selection worked after correcting a native drag-handler conflict with the open picker. Dragging the selected-app widget onto display 2 preserved its size and process selection. Physical show/hide, settings and edit shortcuts, and corner resizing were exercised in the desktop/game session. The final layout changes were followed by the offscreen tests above, not a repeat of those interactive checks.

Cyberpunk 2077 menu and gameplay captures recorded 36,304 and 6,966 Present intervals respectively, with 163 and 113 sensor rows. Both reported no loss and a confirmed final drain. Independent CSV recomputation matched all summary statistics within floating-point rounding. Sequential driver/app samples agreed for utilization, power cap, graphics clock, fan duty and memory totals; GPU temperature uses the integer NVML API. These measurements are application Present cadence, not generated/displayed frames or a gameplay performance benchmark.

The overlay remained visible over the borderless game on its original display, including when unlocked. Actual game output on display 2 has not been validated. No game graphics/display settings were changed. Raw screenshots, game settings and recordings remain local and ignored. The captures folder opened in File Explorer; an earlier reported shell error was not reproduced, but both entry points share recoverable error handling.

The candidate installer preserved settings and all 485 installed payload files matched its portable package. Repeat package/hash checks for the final tagged commit. The remaining coverage limits below apply to this preview release.

## Earlier 0.2 verification

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
