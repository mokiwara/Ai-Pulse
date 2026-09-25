# Packaging, signing, and updates

> **Phase 1 implementation:** `scripts/publish.ps1` makes a self-contained single-file `win-x64` executable for personal use. The published executable launched successfully without a machine-wide .NET 10 runtime. Startup uses HKCU Run. Signing, MSIX, automatic updates, and a public installer remain later packaging work. See [implementation record](17-phase1-implementation.md).

## Proposed release path

Ship a signed per-user **MSIX** package with App Installer update manifest for direct distribution, subject to a Phase 0 packaging spike for WPF, background tray lifetime, startup registration, notification activation, and WebView-free deployment. Microsoft documents WPF packaging with MSIX and App Installer automatic updates. Keep an unpackaged signed installer fallback if MSIX restrictions block reliable notch/tray behavior. [Packaging overview](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [direct distribution](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path), [updates](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview).

Target Windows 11 first; decide exact minimum Windows build after notch and packaging tests. Build x64 first, ARM64 if beta demand justifies it. Pin .NET 10 runtime strategy (self-contained vs framework-dependent) after measuring package size, startup, and servicing tradeoffs. Installer must never migrate or copy provider credential files.

## Release discipline

- Sign executable and package with an organization certificate; verify signature in release CI and on installed files.
- Version Hub, DB schema, and connector protocol independently. On provider CLI version drift, disable the incompatible connector with a repair message rather than parse guessed fields.
- Stage update rings (internal, beta, stable); allow rollback by keeping database backup before schema migration.
- Update while no child CLI query is active; close provider child processes and local status-line bridge cleanly. Do not kill the user's own CLI sessions.
- Uninstall removes Hub data only after user-facing choice for local history/settings; never remove provider CLI homes or status-line scripts without a reversible explicit operation.

No required cloud backend. Static signed release files and update manifest are sufficient for direct distribution. Test on a fresh Windows profile, not only the developer workstation.
