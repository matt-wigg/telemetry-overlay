# Metric definitions and colors

| Metric | Definition / source |
| --- | --- |
| CPU | System busy time from GetSystemTimes deltas. Not Task Manager processor utility; readings can differ. |
| Process CPU | Process CPU-time delta divided by elapsed time and logical CPU count; normalized to total machine capacity. |
| RAM | Physical memory in use: total minus available. GiB means 2^30 bytes. |
| Commit | System commit charge/limit from GetPerformanceInfo, pages multiplied by the reported page size. Optional overlay row and CSV export. |
| GPU | NVML whole-device utilization, first NVIDIA adapter. |
| GPU °C | Driver-reported GPU temperature. Not hotspot or memory junction. |
| Power | Driver-reported GPU power in watts; separate configured power cap. Not wall-socket power. |
| Clock | Current GPU graphics clock in MHz. |
| Fan | NVML fan duty percentage, not RPM. Zero can be normal with fan-stop. |
| VRAM | NVML v2 device memory used/total, GiB; driver-reserved memory is separate in diagnostics/CSV. Legacy v1 fallback may include reservation in used memory. Not application working set. |
| FPS | 1000 / arithmetic mean of successful Present-start intervals in milliseconds. |
| 1% low | 1000 / mean of slowest ceil(N×0.01) intervals. Tools using a percentile have a different definition. |
| P99 | Nearest-rank 99th percentile interval in milliseconds. |
| Graph | Peak interval in each 0.5-second update, on a fixed 30-second axis; reference line is 1000 / target FPS. Missing intervals produce gaps. |

Live statistics use the last 10 seconds and one dominant swap chain. At least two valid samples are required; stale frame data becomes unavailable. Low-percentile values on short samples are statistically weak; the sample count is shown.

## Status colors

- Neutral: ordinary measurement.
- Muted teal: high CPU/GPU utilization (≥90%). This means busy, not broken.
- Amber: configured temperature warning or ≥90% RAM/VRAM occupancy.
- Red: configured critical temperature or ≥98% RAM/VRAM occupancy.
- Muted dash: unavailable data. A zero reading is never invented for a missing API.
- Driver limit flags: thermal / power brake / power cap are named explicitly. A power cap is not proof of a defect.

Warnings require three consecutive samples (normally about 1.5 seconds) and use a two-unit recovery margin. These are display thresholds, not universal safe limits. GPU temperature thresholds are editable. The hardware-reported slowdown threshold appears in System diagnostics when available.

CPU temperature is intentionally absent because Windows has no universal CPU package sensor interface. No ACPI zone is mislabeled as a CPU package sensor.

API references: [NVIDIA NVML device queries](https://docs.nvidia.com/deploy/nvml-api/latest/api/group__nvmlDeviceQueries.html), [Windows GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes), [Windows GetPerformanceInfo](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getperformanceinfo), and [Windows StartTrace permission model](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-starttracew). Provider behavior is also checked against installed Windows event manifests and live measurements; API descriptions alone are not treated as proof of numeric equivalence.

## Exports

Each capture contains `session.json`, `frames.csv`, `sensors.csv` and finalized `summary.json`. The summary selects the most represented swap chain, defines the statistics and reports collection loss. A recording ends at 1,000,000 captured frames or 60 minutes. A limit or collection loss marks the summary incomplete. An interrupted app may leave raw CSV without a summary; do not silently interpret that as complete.
