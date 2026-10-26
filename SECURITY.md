# Security and privacy

Report vulnerabilities using [private vulnerability reporting](https://github.com/matt-wigg/telemetry-overlay/security/advisories/new). Do not include private captures or credentials in public issues. Only the latest preview receives fixes; this project has no response-time guarantee or independent security certification.

Telemetry makes no application network requests. Settings, bounded diagnostics and captures remain local. Exports include process names/IDs and measurements; inspect them before sharing. Public repository ownership and contributor usernames remain public.

The UI stays unelevated. An administrator-approved collector can read cross-process ETW events. The pipe has a random name, a current-user SID DACL, process identity checks and bounded messages. The elevated client explicitly disallows impersonation. Commands select collection and a PID; they do not accept arbitrary programs or output destinations. Windows administrators and malicious software already running as the same user are outside this isolation boundary.

NVML loads only from Windows System32. No sensor driver, service, game injection, remote plugin execution or automatic tuning is installed. Unsupported sensors remain unavailable. The app has no automatic updater. Initial release binaries are unsigned; SHA-256 checksums establish file consistency, not publisher identity.

CI uses GitHub-hosted runners, pinned Actions, read-only default tokens and no repository secrets. CodeQL's job alone receives security-event upload permission. External contributor workflow runs require maintainer approval; never approve a workflow change without inspecting its code. Dependency updates require review.

See [repository controls](docs/REPOSITORY-SECURITY.md) and [validation limits](docs/VALIDATION.md). Secret scanning and pattern checks reduce risk but cannot prove that every possible sensitive value has been found.
