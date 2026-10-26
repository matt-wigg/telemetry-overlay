# Contributing

Use small branches and pull requests. Explain the observed problem, final behavior and validation. Keep UI changes consistent with the square charcoal instrument design. Add meaningful tests for metric semantics, boundary handling or lifecycle regressions rather than tests that repeat an implementation.

Run the Release build and core test suite. For provider/window changes, record Windows/driver version, hardware class, permissions, display scaling and whether the test ran windowed or borderless. Never present synthetic fixtures as hardware measurements.

No cloud telemetry, hidden privileged startup, arbitrary plugin execution, silent driver installation or automatic tuning. Preserve unavailable readings and documented units. Fix measured bottlenecks instead of speculative architecture changes.
