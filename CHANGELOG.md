# Changelog

## 0.3.0 — 2026-10-26

- Separate the measurement widget from Controls; simplify its header, spacing and status footer.
- Organize Controls into five focused pages with collapsed help and refreshable application selection.
- Reassert visible widget z-order without activation so borderless games cannot obscure it merely by reordering windows.
- Re-clamp placement after DPI changes and verify placement, resize persistence and visibility on every connected display.
- Resolve the foreground application immediately when recording starts.
- Add Ctrl+Alt+S for Controls and align compact shortcut keycaps with the footer status.
- Fit the widget and Controls to small desktops; preserve the widget's preferred dimensions and refresh the display selector after topology changes.
- Keep active Controls above the widget and expose the unlocked widget in Alt+Tab for positioning.
- Reuse an already-enabled FPS session when Enable FPS is chosen again; one approval covers application switching.
- Add free placement with a compact header app picker, optional background-process listing and a separate follow-active-app corner mode.
- Track selected process identity across focus changes; reject reused process IDs and mark recordings incomplete when the target exits.
- Align app names and process IDs in the picker, truncate long header names and preserve template bindings for settings dropdowns.
- Let the open app picker receive mouse input while placement is unlocked; do not intercept its item clicks as native dragging.
- Prefer a recently presenting surface over a retired surface with more historical samples; stabilize equal-count selection and explain unavailable FPS with live freshness diagnostics.
- Move unlocked widgets directly without triggering Windows Aero Snap at monitor boundaries; remember the destination display in free mode.
- Share captures-folder opening between Settings and the tray, with recoverable filesystem/shell error feedback.
- Rebuild the FPS section around a single headline, clear Frame time / Low FPS labels, and a tall subdued history graph behind the readout. Validate live, waiting and stale layouts offscreen at narrow and enlarged sizes.

## 0.2.0 — correctness and security review

- Place delayed ETW samples into graph buckets by event time.
- Drain trace buffers before finalizing recordings; enforce start/stop boundaries and honest completeness flags.
- Bound IPC lines while reading, authenticate collector process identities and prevent elevated-client impersonation.
- Preserve invalid settings during recovery, roll back startup changes on failed saves and keep resized panel dimensions.
- Improve monitor labels, row selection, minimum-height validation, metric-column spacing and dropdown contrast.
- Correct blank Controls rendering on captured desktops and use shorter labels at narrow panel widths.
- Establish clean public source history, pinned CI/security workflows and documented repository controls.

## 0.1.1 — launch visibility fix

- Explicitly restore Controls after background startup and on a second launch.
- Verify the Controls HWND is visible, in addition to the overlay HWND.
- Capture metadata follows the application version; release packaging starts from a clean output directory and includes notices for the bundled runtime version.

## 0.1.0 — initial preview

- Native tray overlay, global shortcuts, monitor placement, drag/resize and tabbed Controls.
- Direct Windows/NVIDIA telemetry with explicit capability limits.
- Native DirectX ETW present-cadence collector and bounded benchmark exports.
- Warning hysteresis, editable theme and documented metrics.
- Self-contained Windows build, installer scripts, 24 tests and a prepared CI template.
- Corrected reserved-VRAM accounting, stale process values, missing graph gaps and second-launch activation.

This is an initial preview, not a completed universal hardware/game certification.
