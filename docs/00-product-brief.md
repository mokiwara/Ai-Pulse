# Product brief

## User job

See the next useful limit signal in under a second, then inspect all existing AI accounts with one click. A provider may expose no quota, one window, several windows, credits, costs, or only a reset time. The app must never invent a five-hour, weekly, monetary, or message-count maximum.

## Three surfaces

| Surface | Content | Boundary |
| --- | --- | --- |
| Closed notch | Icon, one short account/window warning or neutral account count, freshness cue | No charts or all-account scrolling |
| Expanded notch | Provider groups, account rows, each reported window and reset, connection state, Open App/Settings | Virtualize or scroll after a small visible set |
| Full app | Overview, Accounts, Alerts, Settings | No SaaS analytics dashboard |

Account labels are user aliases, not claims about provider plan. Show `OpenAI > Work > Codex weekly > 86%, resets Sat 09:00` only when every datum is actually reported. A source that supplies percentage but no absolute cap shows a percent bar without `used / limit`. Use local time with an accessible exact timestamp tooltip.

## Existing-auth discovery is the normal path

First launch checks installed CLIs and known local config roots, then invokes safe read-only status interfaces. The user may add a **path to an already authenticated provider context**; that is not a login. Never change another app's active account. Detecting an installed ChatGPT or Claude Desktop app does not imply access to its usage or authentication. Never show “3 accounts found” unless three independent contexts have actually been verified.

Do not read provider credential files, copied tokens, or browser cookies. Do not use embedded provider login in the MVP. Any future WebView workflow needs provider permission and an explicit user choice; see [ADR-005](adr/ADR-005-web-authentication.md).

## Product rules

1. A provider account is a verified provider identity; a metered product is a separate scope. Several local tools may report the same account/product quota. Until a stable identity is safely returned, keep local contexts separate and mark identity provisional. Never add percentages across shared surfaces.
2. Status is explicit: fresh, refreshing, stale, offline, authentication required, rate limited, unsupported, or error.
3. Stale readings keep their timestamp and cannot trigger a new threshold alert. Unknown values render as unavailable, never zero.
4. Alert language names provider, account, product surface, and window.
5. The app must work with 2–6 typical accounts and remain responsive with dozens.

## Success measures

- Proven, distinct accounts show independent readings without provider account switching.
- Closed notch does not interrupt typing, fullscreen work, or Windows snap.
- A source failure is visible within one scheduler cycle; last values stay labeled stale.
- No secrets enter the AI Usage Hub database, diagnostic logs, crash reports, or support export.
