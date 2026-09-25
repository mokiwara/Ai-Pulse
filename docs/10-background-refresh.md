# Background refresh and freshness

> **Phase 2 implementation:** Codex keeps active polling; Claude checks a local status-line feed every 30 seconds and probes auth no more than every 10 minutes automatically. A manual check cannot force new Claude quota data. Last successful snapshots survive failures. See [implementation record](18-phase2-implementation.md).

> **Phase 1 implementation:** Codex uses a 2-minute default interval (configurable 2–60), a 30-second due-job timer, serialized reads, exponential failure backoff, a 15-minute freshness limit, and on-demand reads after launch/resume/network return or opening an old notch. A failed read retains the last valid snapshot and makes it stale. See [implementation record](17-phase1-implementation.md).

## Scheduler

One per-user scheduler maintains jobs keyed by `(connector, localContext)`. Each connector supplies a conservative `minimumInterval`, `preferredInterval`, timeout, and whether it is pull, activity-driven, or manual. The scheduler enforces global concurrency (initially 2), per-provider concurrency (initially 1), jitter, exponential backoff, `Retry-After`, and cancellation at shutdown. User manual refresh can bypass preferred interval but not provider minimum or rate-limit backoff.

| Source | Initial policy (hypothesis for spike) | Why |
| --- | --- | --- |
| Codex App Server `account/rateLimits/read` | On startup and every 5–10 minutes while online; immediate when panel opens only if older than 2 minutes; adjust after live testing | Structured pull through already-authenticated CLI, but experimental command and provider load unknown. [App Server](https://learn.chatgpt.com/docs/app-server) |
| Claude Code status-line feed | Receive only on existing CLI events; no polling, no synthetic prompt | Source is activity-driven and may be absent until first API response. [Status line](https://code.claude.com/docs/en/statusline) |
| Claude official Settings > Usage | Manual Open Usage page | No supported programmatic personal quota source. [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices) |
| Future OpenAI/Anthropic API organization | Several minutes, scoped by API guidance, opt-in | Reports are aggregated and delayed; not MVP. [Anthropic Usage & Cost](https://platform.claude.com/docs/en/manage-claude/usage-cost-api) |

## Snapshot state

`fresh` means successful observed value younger than the connector's `freshFor`; `refreshing` overlays last known status; `stale` means age exceeded or last attempt failed; `offline`, `authentication-required`, `rate-limited`, `unsupported`, and `error` explain why refresh is unavailable. Store `lastSuccessAt`, `lastAttemptAt`, `sourceObservedAt`, and `nextRetryAt` separately. A source can return valid account identity but no quota windows; that is `unsupported` or `partial`, not fresh 0%.

Never use local wall-clock time to invent a provider reset. If a known `resetAt` passes before a new sample, mark the window expired and suppress its previous percent. Display “Checking after reset” until a fresh reading arrives. Alert engine evaluates only newly fetched, provider-confirmed windows. On sleep/wake and network restoration, stagger contexts and recompute freshness; on clock/timezone change, re-render timestamps without modifying UTC data.

## Failure handling

Classify errors as auth, rate-limit, offline/DNS, provider unavailable, version/schema mismatch, process crash, timeout, or unknown. Hide raw provider stderr from UI and logs. Keep last successful snapshot but clearly label age. After repeated schema mismatch, disable automatic refresh for that connector version and show a repair/update action. A connector may downgrade its capability, but must not replace missing fields with zeroes.

## Alerts

Rules can target one account/surface/window ID, a semantic kind across selected accounts, or a provider group. Defaults are off until the user enables 70/85/95% or custom thresholds. Evaluate each fresh window independently: `Claude > Personal > subscription weekly reached 85%`. Fire once on upward crossing, then re-arm after a confirmed reset or a drop below a hysteresis band; dedupe across multiple local source contexts reporting the same shared quota. A separate limit-reached event requires a provider-confirmed reached state or fresh 100% reading, never a stale prediction. Auth-expired and connector-broken are connection events with their own cooldown. Reset-occurred requires an observed new window/reset, not only the local clock passing the old reset time. Support quiet hours and per-rule notification channel; account details keep recent alert history.
