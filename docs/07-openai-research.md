# OpenAI and Codex research

**Research baseline:** 24 September 2026. Official OpenAI documentation plus the official [openai/codex](https://github.com/openai/codex) source. Live reads were subsequently performed during Phase 1 with Codex CLI 0.156.1 across three distinct accounts; see [implementation record](17-phase1-implementation.md). Codex `/status` shows percent left, while App Server's `usedPercent` is percent consumed; Hub now displays percent left explicitly.

## Product boundaries

| Surface | Auth/identity | Usage source | What is available | What is not established |
| --- | --- | --- | --- | --- |
| ChatGPT consumer chat | Existing web/desktop login | Product UI; model selector/usage UI varies by plan/model | Some reset dates and limits in UI | General machine-readable current chat quota; a single cross-model percentage. [Help](https://help.openai.com/en/articles/9824962-openai-o1-o1-mini-and-o3-mini-usage-limits-on-chatgpt-and-the-api) |
| Codex CLI | Existing Codex-managed ChatGPT login | `codex app-server` JSONL protocol | Account email/plan when returned; rate-limit bucket percentages, durations, resets, optional credits/reached state | Universal five-hour/weekly buckets or absolute request maximum. [App Server](https://learn.chatgpt.com/docs/app-server) |
| ChatGPT Desktop Codex agent | By default native Windows Codex home | Same Codex App Server context, if available | Codex quota, not entire ChatGPT chat usage | Separate desktop IPC quota API. [Windows app](https://learn.chatgpt.com/docs/windows/windows-app) |
| OpenAI API | API organization/project authority | Organization Usage, Costs, spend-limit endpoints | Aggregated API token/request usage and costs, scoped by dimensions; separate spend controls | ChatGPT subscription quota. [Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [spend limit](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/spend_limit/methods/retrieve) |

OpenAI [states API billing and ChatGPT Business subscriptions are separate](https://help.openai.com/en/articles/8542115). Organization Usage/Costs data must never be labeled “ChatGPT subscription remaining.”

## How Codex `/status` obtains quota

The current open-source TUI's `refresh_rate_limits` launches a background **App Server** request `GetAccountRateLimits`, which maps to documented `account/rateLimits/read`. The TUI uses the result for `/status` and periodic refresh; it can also receive sparse `account/rateLimits/updated` notifications. [TUI request source](https://github.com/openai/codex/blob/main/codex-rs/tui/src/app/background_requests.rs), [App Server protocol](https://learn.chatgpt.com/docs/app-server), [protocol types](https://github.com/openai/codex/blob/main/codex-rs/app-server-protocol/src/protocol/v2/account.rs).

The current Codex backend client performs a GET for usage through a Codex-owned authenticated client and maps the response into rate-limit snapshots; other Codex code also parses response headers/events. This is evidence of a fresh underlying read path, **not** a supported third-party HTTP API. The backend URL and token handling are implementation details. AI Usage Hub should speak only to the local provider-owned App Server. [Backend client source](https://github.com/openai/codex/blob/main/codex-rs/backend-client/src/client/rate_limit_resets.rs), [Codex parser](https://github.com/openai/codex/blob/main/codex-rs/codex-api/src/rate_limits.rs).

The documented result has `rateLimits` (legacy single bucket) and optional `rateLimitsByLimitId` (multiple buckets). Each bucket can have `primary` and `secondary` windows with `usedPercent`, `windowDurationMins`, and `resetsAt`. `planType`, credit fields, and reached state are conditional. `account/read` may return `email` and `planType`. `account/usage/read` is a **separate token activity** summary, not quota consumption. Do not double-count `rateLimits` when the same bucket appears in `rateLimitsByLimitId`. [App Server rate limits](https://learn.chatgpt.com/docs/app-server).

### Integration choice and alternatives

| Option | Classification | Decision |
| --- | --- | --- |
| Spawn `codex app-server` over stdio for each known `CODEX_HOME`, call read-only JSON-RPC | Documented structured interface; CLI command is **experimental** | Preferred Phase 0 target; pin/test minimum version, restart safely, never call login/logout |
| `codex login status` | Documented CLI command | Discovery/auth mode only, no quota. [Command reference](https://learn.chatgpt.com/docs/developer-commands) |
| `codex exec --json` | Documented task mode | Not a quota query; could consume model usage; reject |
| Parse `/status` terminal text | TUI presentation | Reject while structured App Server path works |
| Read session JSONL / local cache | Local implementation detail | May contain past snapshots; no documented fresh quota contract; not primary. [Config/state docs](https://learn.chatgpt.com/docs/reference/troubleshooting) |
| Call private Codex backend with CLI token | Reverse engineering | Reject: extracts provider auth and relies on undocumented endpoint |

The App Server docs show JSONL over stdio, the required initialize handshake, and `account/rateLimits/read`. The CLI reference calls `codex app-server` experimental and says it may change without notice. Ship only after a live version matrix proves it sufficiently reliable; otherwise show Codex as unsupported or use a clearly labeled passive fallback. [App Server](https://learn.chatgpt.com/docs/app-server), [CLI maturity](https://learn.chatgpt.com/docs/developer-commands).

## Existing accounts and desktop interaction

Codex uses `CODEX_HOME` for its state; the Windows ChatGPT app uses `%USERPROFILE%\.codex` by default. The CLI and extension share cached login for a given home. `--profile` layers configuration and is **not evidence of separate authentication**. A separate `CODEX_HOME` may represent another existing login, but official docs do not provide an enumeration API for all homes. Discover default and current process environment; let users register other existing roots. Verify each with `account/read`, never open `auth.json`. Do not switch credentials in the default home. [Windows app](https://learn.chatgpt.com/docs/windows/windows-app), [auth storage](https://learn.chatgpt.com/docs/auth), [profile flag](https://learn.chatgpt.com/docs/developer-commands).

## OpenAI API later connector

The organization Usage API exposes endpoint-specific bucketed counts (for example completions input/output tokens and request count) with grouping such as project, model, API key, or user where supported; Costs reports monetary amounts. The organization spend-limit read is a separate endpoint. Use an organization-authorized mechanism only if the user later opts into an API connector; it cannot meet the zero-credential subscription MVP. Determine permission requirements and whether an existing official CLI can query these endpoints without handling keys in a future spike. [Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [costs](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage/methods/costs), [spend guidance](https://help.openai.com/en/articles/6614457-troubleshooting-api-usage-and-spend-limits).
