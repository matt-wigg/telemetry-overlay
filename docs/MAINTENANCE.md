# Maintenance

1. Pull `main` with a clean working tree and create a short feature branch. Git does not sync uncommitted files.
2. Make focused changes and regression tests. Use a privacy-safe commit identity. Never commit local settings, captures, screenshots, diagnostics, machine profiles, credentials or build output.
3. Open a pull request. Run `build/Verify.ps1` locally; use `-IncludeFps` for collector changes. CI runs locked restores, both test projects, packaging, CodeQL and pull-request dependency review.
4. Review behavior, metric definitions, permissions and documentation. Resolve conversations and merge passing changes using squash merge.
5. Update the version and changelog in a reviewed change. Build releases from that exact clean commit with `build/Publish.ps1`. Inspect packages and test installation before release.
6. Create a new version tag and a **draft** prerelease. Upload the installer, portable ZIP and SHA256SUMS; inspect all assets before publishing. Immutable releases lock published assets and the associated tag, so corrections require a new version.

The owner retains an explicit pull-request review bypass for solo maintenance; this is not access for public visitors. CI, force-push and deletion restrictions still apply under the branch rule. Avoid bypassing review except for an owner-authored, locally reviewed change. Do not grant write access merely to accept contributions.

Dependabot proposes dependency/Action updates weekly; it never auto-merges them. Review .NET SDK and bundled runtime servicing releases regularly: self-contained binaries need rebuilding to receive runtime fixes. Keep Action references pinned to full commit SHAs and NuGet lockfiles committed.

The application has no unattended updater. Quit before manually installing an update. Preserve `%LOCALAPPDATA%\Telemetry`; incompatible settings are preserved and explicit recovery keeps a backup. Back up important captures separately from source control.

Repository settings live in GitHub as well as these documents. Verify them after a transfer, recreation or ownership change. Use [the security configuration](REPOSITORY-SECURITY.md) as the review checklist; local Git files alone cannot enforce server permissions.
