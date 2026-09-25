# AI Pulse 1.0.5 release verification

Windows release prepared on 2026-09-25. The installer selects x64 or ARM64 and installs per user without administrator privileges. Both portable builds include .NET. Each recipient supplies their own provider CLI installations and accounts; release packaging selects compiled executables and public notices only.

## Changes

- Codex executable discovery now checks the installed entry point, versioned local packages, and current, user, and machine PATH values at each refresh. If one executable cannot launch, the connector tries the next candidate.
- A failed launch is labeled separately from a missing CLI and records only a safe exception type and numeric error code. Missing or unlaunchable Codex retries are capped at five minutes instead of thirty.
- The notch refresh control uses a drawn icon centered in the click target, with a subtle hover state. Its capsule remains centered on the selected display.
- The capsule has a manual refresh button. It starts a full account refresh without expanding the notch or taking focus from another window, and shows a brief busy state.
- The collapsed notch displays one chosen account per provider, with remaining five-hour and main weekly percentages. A weekly-only account displays one percentage. Settings also support automatic selection and hiding either provider.
- Automatic refresh can be set to 1, 2, 5, 10, or 15 minutes. The scheduler checks due work every 10 seconds.
- A failure to launch an installed Codex CLI is reported separately from a missing CLI.
- Version probing now waits for child termination and stderr cleanup.
- Failed database initialization disposes the connection before propagating the error.
- Removing terminal capture bounds configuration reads, detects edits made while preparing the replacement, and cleans temporary files. The confirmation now accurately describes the integration change. A file comparison cannot eliminate every race with an unrelated writer.
- Capture helpers enforce a five-second main-thread input deadline even when Console.In ignores cancellation. The executable smoke test deliberately holds stdin open to catch this regression.
- Release builds enforce locked application dependencies, vulnerability-audit/build warnings as errors, automated tests and a published executable smoke test. Single-file analysis keeps the publishing tool dependency in the lock file.

## Verification

- 69 automated tests pass, including discovery across install and updated PATH locations, launch fallback, retry timing, safe diagnostics, selected-account notch formatting, weekly-only display, settings persistence, malformed/oversized provider data, cancellation lifetime, corrupt snapshots, unchanged custom provider integrations, capture removal and shortcut behavior.
- The 1.0.5 local upgrade preserved all four saved contexts. The installed x64 executable matched the published payload; launched with a deliberately restricted inherited PATH, it refreshed all three saved Codex contexts without errors.
- Fixture UI rendering verified the account selectors and the `5h/weekly%` and weekly-only capsule layouts. A real click on the refresh button invoked refresh once while the notch stayed collapsed and foreground focus stayed elsewhere. The icon and notch centers were checked against their button and display bounds.
- Prior repeated UI lifecycle test: 65 cycles; all 130 windows collected. Final run: managed memory 3,221,960 to 3,261,480 bytes; handles 2,135 to 2,137 after warm-up. This is a bounded regression test, not a multi-day leak guarantee.
- NuGet advisory check reported no known vulnerable direct or transitive dependencies from its configured source on the test date.
- x64 published helper: normalized capture, raw session data exclusion, invalid/oversized input preservation and idle-stdin termination. Results are recorded in portable-capture-smoke.json in the source checkout.
- Existing 1.0.0 install/upgrade/uninstall test evidence is retained in installer-check.txt. The 1.0.5 installer uses the same installation logic; a local upgrade was tested over this machine's existing real installation, without uninstalling it.

## Earlier Codex discovery incident

After an installer launch, all three saved Codex contexts reported `CODEX_NOT_FOUND` from 15:40 to 16:00 UTC. The Codex executable existed before the incident. Restarting the same AI Pulse 1.0.1 build restored all three contexts. The old connector mapped both failed executable discovery and failed `Process.Start` to `CODEX_NOT_FOUND`, and logged no native error detail, so the exact transient launch condition cannot be recovered. Its generic failure backoff could delay automatic recovery for up to 30 minutes. Version 1.0.5 corrects the classification, discovery and retry behavior, and records a safe numeric launch error for a future recurrence.
- A static Codex Security review of the pre-fix first-party application source completed with no confirmed exploitable vulnerabilities. External CLI implementations, dependency internals and installed ACLs were outside that review. Subsequent changes above were inspected and tested separately. Scan usage reported by the tool: 2,135,319 total tokens, including 2,037,504 cached input tokens.

## Remaining release limitations

The executables are unsigned. A publisher-authenticated public release requires a code-signing certificate and timestamped signatures. SHA-256 manifests check integrity, not publisher identity. Do not disable Windows security features to run the app.

ARM64 is cross-compiled; this x64 machine cannot provide a native ARM64 runtime test. Clean-machine Windows/ARM64 installation and multi-day idle/sleep/resume testing remain release validation work. No claim of zero defects or zero vulnerabilities is made.

This is a Windows application, not an executable for macOS, Linux, Android or iOS. Use a supported Windows edition. Microsoft's current .NET Windows support matrix is at https://learn.microsoft.com/en-us/dotnet/core/install/windows . The installer's Windows 10 1809 version floor is a technical minimum, not a promise that every Windows 10 edition remains supported.

The installed CLI must support the required account/usage commands. Provider command formats can change; failed readings are shown as unavailable or stale. Authentication stays with provider tools. Optional terminal capture modifies only its selected provider configuration; user-composed commands remain user-managed.
