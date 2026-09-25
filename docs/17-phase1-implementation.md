# Phase 1 implementation record

**Date:** 24 September 2026. This records the implemented OpenAI/Codex Windows application. The earlier research, ADRs, capability matrix, and risk register remain the design background. At the user's direction, Phase 1 now means the complete usable Codex app; Claude is Phase 2.

## Shipped shape

One .NET 10 WPF executable owns the full app, non-activating notch, tray icon, refresh scheduler, Codex connector, and SQLite store. The layout is intentionally compact:

```text
src/AIUsageHub.App/       WPF surfaces, Win32 interop, connector, domain, persistence, scheduling
tests/AIUsageHub.Tests/   normalization, state, SQLite, context, scheduler, alert, optional live tests
assets/icon.ico           Windows/tray icon
scripts/run-dev.ps1       development launch
scripts/publish.ps1       self-contained win-x64 publish
```

The planned four production assemblies were consolidated into one because Phase 1 has one connector and a small utility UI. Domain records and connector/SQLite code remain separate files. No generic plugin system or dependency-injection framework was added. The full app has **Overview, Accounts, Settings**; alerts live in Settings as requested.

## Codex protocol verified live

Codex CLI **0.156.1** on this development machine accepted `codex app-server --stdio` with a UTF-8 JSONL `initialize` request, `initialized` notification, `account/read` with `refreshToken:false`, and `account/rateLimits/read`. The current authenticated context returned a ChatGPT account type and a `rateLimitsByLimitId.codex` bucket with 300-minute and 10080-minute windows and Unix-second reset times. The same bucket also appeared in legacy `rateLimits`; Hub prefers the multi-bucket map to avoid duplicate bars. Live reads returned real changing percentages. Neither absolute request caps nor a model-to-bucket mapping were returned.

The connector uses the discovered absolute `codex.exe`, sets `CODEX_HOME` per child, removes known API/access-token environment overrides, sends no model turn, discards stderr, caps response lines, times out after 30 seconds, and kills the child process tree in a `finally` block. It never opens Codex auth files. A UTF-8 BOM initially caused the live App Server to ignore the first request; the writer now emits UTF-8 without a BOM. This was confirmed with a separate local protocol probe and an opt-in live integration test. No raw account response or email is logged or stored; a fingerprint is stored when Codex supplies an email.

Four local Codex homes were found: `.codex`, `.codex-cli`, `.codex-cli-account2`, and `.codex-cli2`. Read-only probes returned **three distinct** authenticated account fingerprints; the last two homes share one account. Hub now scans conventional `.codex*` homes directly under the user profile, verifies each through Codex, and auto-registers one context per distinct account. `CODEX_HOME` is included, and a home elsewhere can be selected manually. Removing a context stores only its excluded path so the next scan does not silently re-add it; adding that path explicitly clears the exclusion. There is still no Codex API that enumerates every arbitrary home.

## State and refresh

- SQLite stores context aliases/paths, latest normalized snapshots, settings, and alert dedupe keys. No provider credentials or raw source JSON are stored. Cached readings start stale until a successful refresh.
- The default refresh interval is 2 minutes and is configurable from 2 to 60 minutes. A 30-second timer only checks due jobs and freshness; reads are serialized and get failure backoff. Launch, resume, network return, manual refresh, and opening an old expanded notch can request earlier reads. Rate-limited reads retain their backoff.
- Codex App Server returns `usedPercent`, while Codex `/status` shows percent left. The UI now labels and displays `100 - usedPercent` as **% left**; alert thresholds continue to use the source's consumed percentage. Window kind comes from `windowDurationMins`, not primary/secondary position.
- The first `.codex-cli2` notch capture showed 41% beside 5-hour and 51% beside weekly. Those were unlabeled raw `usedPercent` figures; the historical `/status` screen was not captured, so its exact displayed numbers cannot be independently confirmed. A later live `.codex-cli2` read at 18:49 UTC returned 76% used for the 300-minute window and 57% used for the 10080-minute window, now displayed as 24% and 43% left. The CLI's folder-trust prompt prevented a direct `/status` TUI comparison without changing its configuration. Normalization tests cover reversed primary/secondary field positions by using duration metadata.
- Failed reads preserve the last successful numbers with a stale/error label. After a reported reset passes, the old percentage is hidden until Codex confirms a new reading.
- Alerts use fresh readings only, fire once per threshold/window/reset identity, and can notify on a confirmed new reset window. Contexts with the same verified account fingerprint share alert dedupe.

## Windows behavior

The notch is one transparent WPF HWND with `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, no taskbar presence, and non-activating `SetWindowPos`. It centers in physical screen coordinates for the selected display, recomputes after display/DPI/settings changes, and falls back to primary if the display disconnects. It animates only on expand/collapse. A two-second window-style heuristic hides it when a borderless fullscreen foreground window occupies the selected display. The tray stays present. The full app closes to tray; Exit shuts down the WPF process and cancels connector reads. Startup uses the current user's `Run` registry key and needs no administrator rights.

## Verification and limits

- `dotnet build AIUsageHub.slnx`: clean, zero warnings after updated SQLite packages.
- `dotnet test AIUsageHub.slnx`: 14 tests pass, including normalized multi-bucket/weekly-only parsing, reversed primary/secondary duration positions, protocol IDs/errors, remaining display, resets, freshness, SQLite, context removal, discovery candidates, scheduler, and alert dedupe. Set `AIUSAGE_LIVE_TEST=1` to include a live Codex read.
- A self-contained single-file `win-x64` executable published and launched without the machine-wide .NET 10 runtime. The final run loaded three distinct authenticated account contexts and persisted three real normalized snapshots to SQLite with no context errors. SQLite migrations 1 and 2 completed; the second keeps removed paths excluded from automatic discovery.
- UI render capture confirmed the Overview and both notch states displayed real percentages left and reset times. Notch placement was checked on primary (2560×1440) and secondary (1920×1080) displays; a nonexistent selected display fell back to primary. A simulated borderless fullscreen window hid the notch, and the notch reappeared after it closed. `WM_MOUSEACTIVATE` handling kept another window in the foreground when the notch was clicked. Mixed-DPI and physical display disconnect/reconnect remain hardware checks.
- The startup checkbox created and removed the per-user `Run` value in a live UI check. Closing the main window left the tray and notch running; a second launch reopened the existing instance; Exit shut it down. In a 10-second idle sample after refresh, CPU time did not advance, working set was about 206 MB, and there were no app-owned child processes.
- Windows tray balloons depend on the user's Windows notification settings. Fullscreen detection is heuristic. The App Server command remains experimental. This build is unsigned and has no installer/update channel.

Phase 2 is implemented in [18-phase2-implementation.md](18-phase2-implementation.md). The zero-credential boundary remains in place.
