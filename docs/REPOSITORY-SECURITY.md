# Public repository controls

The repository is public and MIT licensed. Anyone can read, clone, fork and propose a pull request. Public visibility does not grant write or administrative access. The maintainer decides what is merged and released.

## Server configuration

- No additional collaborators, deploy keys or repository webhooks are required.
- `main` requires an up-to-date Windows build/test check, CodeQL and dependency review, resolved conversations and a pull request with one code-owner approval. Stale approvals are dismissed. An explicit owner-only review bypass permits solo maintenance; other protections include administrators.
- Force pushes and branch deletion are disabled. Linear history and squash merging are used; merge commits, rebase merging and automatic merge are disabled.
- Release immutability protects published tags/assets. New versions are published from drafts after all assets have been uploaded and inspected.
- Secret scanning, push protection, Dependabot alerts/security updates and private vulnerability reporting are enabled where GitHub provides them for this public repository.
- Actions use a selected allowlist of official GitHub/Actions repositories. Default workflow tokens are read-only and cannot approve pull requests. All external contributors require workflow approval. Fork pull requests receive no repository secrets and no writable content token.
- Unused wiki, discussions and project features are disabled. Issues and fork-based contributions remain available.

The enforcement source is GitHub's repository settings, not this document. Owners can change policy; a compromised owner account remains a risk. Protect that account with strong authentication and review connected apps separately. The repository does not alter account-wide settings.

## Source and release hygiene

Use project attribution for maintainer commits and tags, with no private email, workstation path or hardware profile. Keep local backups outside tracked source. Source guard checks common credential/path formats; GitHub scans supported secret types. Review all public package contents and documentation before release.

The initial public history is a clean root. Recreating a repository does not revoke earlier downloads or copies held elsewhere. If a real credential is ever exposed, revoke/rotate it before treating content removal as remediation.
