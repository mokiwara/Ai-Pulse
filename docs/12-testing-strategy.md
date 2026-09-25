# Testing strategy

> **Phase 1 implementation:** `tests/AIUsageHub.Tests` contains sanitized Codex multi-bucket fixtures and tests for normalization, missing fields, reset timestamps, freshness, SQLite persistence, context independence/removal, refresh timing, and alert dedupe. Setting `AIUSAGE_LIVE_TEST=1` before `dotnet test` includes the current authenticated Codex context. Three distinct accounts were verified live via App Server. Primary and secondary display placement, missing-display fallback, fullscreen hide/reappear, UI rendering, and self-contained publish were tested manually. Mixed-DPI and physical display disconnect remain hardware gaps. See [implementation record](17-phase1-implementation.md).

## Contract over screenshot

Test normalized domain rules with source-shaped fixtures, not fabricated provider limits. A Codex fixture must cover `rateLimitsByLimitId`, legacy duplicate bucket, absent secondary, non-five-hour durations, credits, null reset, and identity change. A Claude fixture must cover missing `rate_limits`, one missing window, reset expiration, API-billed auth mode, and stale active-session data. No test expects a five-hour or weekly window merely because another provider supports it. Pin fixtures to a CLI version and source URL; update them only after a version review. [Codex App Server](https://learn.chatgpt.com/docs/app-server), [Claude status-line schema](https://code.claude.com/docs/en/statusline).

## Test layers

| Layer | Required checks | Gate |
| --- | --- | --- |
| Core unit | Window normalization, null semantics, percent math only with same-scope cap, dedupe, stale transitions, alert edge/cooldown/reset logic | Every change |
| Connector contract | Parse real documented JSON shapes; reject schema drift/oversized output; never call login/logout; child env/context isolation; no credential file reads | Every connector change |
| Provider integration | Live read-only Codex App Server against test account/context and separate known homes; Claude status-line feed from active session; rate/terms review | Phase 0 and release candidate, opt-in machine |
| Windows UI | Multi-monitor/DPI/fullscreen/taskbar/sleep-wake/focus/keyboard/high contrast; 2 and 30 accounts | Phase 1 and beta hardware matrix |
| Security | Secret canaries in fake CLI stdout/stderr absent from DB/log/export; malicious path/executable rejection; no network listener; ACLs | Release gate |
| Packaging | Clean Windows 11 install/update/uninstall, startup entry, notification activation, data migration, signed package | Release gate |

Use a fake CLI executable and a fake local status-line sender for deterministic integration tests. These tests verify process boundaries and UI semantics without any provider credentials. Live provider tests are a separate manual/CI-disabled suite and never record raw auth or account output. A successful live test records only pass/fail, client version, fields present, and redacted timing.

## Acceptance scenarios

1. Three registered Codex roots: independent identity and quota query, no default account change; missing third root is reported honestly.
2. Claude Code idle 20 minutes: last 5-hour/weekly values visibly stale and no new threshold notification.
3. Codex returns only a 7-day window: UI shows only weekly. An arbitrary 15-minute bucket receives a generic duration title.
4. One account's 85% weekly alert does not fire for another account's 85% 5-hour window.
5. Provider process hangs or exits: Hub remains responsive, old snapshot labeled stale, retry backoff respected.
6. Monitor unplug, 100%→200% DPI move, exclusive fullscreen, taskbar relocation, lock/sleep/wake: notch recovers without trapping focus or covering a game.

Do not create tests that merely mirror implementation methods; test observable behavior and source contract boundaries.
