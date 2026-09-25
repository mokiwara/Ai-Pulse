# Claude and Anthropic research

> **Implementation update, 25 September 2026:** Claude Code 2.1.280 still documents the status-line `rate_limits` fields, subject to first-response and activity freshness limits. Phase 2 uses this passive source and `claude auth status`; no private API or Desktop IPC was added. The default Claude Code context on this machine is logged out, so live quota comparison remains unavailable. See [implementation record](18-phase2-implementation.md).

**Research date:** 24 September 2026. Claude.ai, Claude Desktop, Claude Code, and Anthropic API are distinct product surfaces, though the first three may draw from one subscription pool.

## Subscription quota

Anthropic says usage of Claude web, Desktop, and Claude Code counts toward the same subscription limit. Settings > Usage shows five-hour session and weekly progress/reset; model-specific weekly windows appear when the plan includes them, and usage credits are separate. A usage-based Enterprise plan may lack a fixed personal quota. [Shared limits](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work), [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices), [credits](https://support.claude.com/en/articles/12429409-manage-usage-credits-for-paid-claude-plans).

No official personal subscription Usage REST/SDK API or Claude Desktop local usage IPC was located. The official page is an excellent **human-readable fallback**, but AI Usage Hub cannot safely turn it into an automatic connector without a separately approved, terms-compatible interface. Opening the page in the user's normal browser does not give Hub programmatic data; copying cookies or injecting scripts into another process is rejected. [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices), [Anthropic third-party guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account).

## Claude Code: what is structured

| Mechanism | Fields | Freshness / caveat |
| --- | --- | --- |
| `claude auth status` | JSON login state; provider/method may be present | Safe auth probe, no usage. [CLI reference](https://code.claude.com/docs/en/cli-reference) |
| `/usage` (aliases `/cost`, `/stats`) | Plan limits and session/activity details in interactive UI | No documented machine-readable quota mode. [Commands](https://code.claude.com/docs/en/commands) |
| Status-line JSON stdin | `rate_limits.five_hour/seven_day.used_percentage`, `resets_at`; conditional spend limit behind apps gateway; session cost estimate | Officially documented; Pro/Max subscription fields appear only after first API response; each can be absent and expire. Data can be old while session idle. [Status line](https://code.claude.com/docs/en/statusline) |
| `claude -p --output-format json` / Agent SDK | Model turn output, token usage, session costs | **Not** a `/usage` quota command; incurs model work and may consume allowance. [CLI reference](https://code.claude.com/docs/en/cli-reference) |
| Local credentials, transcripts, state | Provider-owned files | Do not inspect credentials; no documented fresh quota file. [Authentication](https://code.claude.com/docs/en/authentication) |

The status-line contract is usable as an optional **passive feed** from an existing active CLI session. Hub can provide a local helper that forwards only `rate_limits` and minimal identity/context data into an ACL-protected local channel. It must not silently overwrite a configured status line. The user can opt to compose it with their current script, or the account remains identity-only. Because status-line data is session-scoped and event-driven, annotate source timestamp and stale state; don't present it as a fresh independent account poll. If multiple sessions for one account disagree, choose the most recently observed sample and show its age. Official docs specify event triggers and absence conditions. [Status line](https://code.claude.com/docs/en/statusline).

## Existing multiple accounts

`CLAUDE_CONFIG_DIR` is officially documented to support side-by-side Claude Code accounts; credentials and state reside under that directory. On Windows the default is `%USERPROFILE%\.claude`. AI Usage Hub can probe the default and user-supplied known context roots by running `claude auth status` with an explicit environment, without reading `.credentials.json`. It cannot safely infer all custom roots or assume one Desktop profile per CLI root. A status-line payload must be bound to its originating context; if identity cannot be verified, label it unresolved rather than merging by email guess. [Environment variable](https://code.claude.com/docs/en/env-vars), [auth storage](https://code.claude.com/docs/en/authentication).

Anthropic documents precedence between environment API keys and subscription login; a CLI process can be billed to API even while a subscription login exists. `Probe` must report the effective auth mode and refuse to label API-billed usage as subscription quota. Never change the user's environment to force a subscription mode. [Authentication](https://code.claude.com/docs/en/authentication), [Pro/Max guide](https://support.claude.com/en/articles/11145838-use-claude-code-with-your-pro-or-max-plan).

## Anthropic API later connector

The official Usage & Cost Admin API provides organization API token/cost history (not personal Claude quotas), usually within five minutes, with an Admin API credential or equivalent permitted authority; individual accounts cannot use the Admin API. Claude Enterprise analytics is a different API/key and can have much longer reporting lag. Both remain later privileged integrations. [Usage & Cost](https://platform.claude.com/docs/en/manage-claude/usage-cost-api), [analytics distinction](https://platform.claude.com/docs/en/manage-claude/analytics-api).

## Terms and reliability

Anthropic's official guidance prefers API-key access for third-party products and forbids misrepresenting client identity or routing third-party traffic against subscription limits. A Hub connector that extracts Claude OAuth tokens to call a private quota endpoint is excluded. HTML extraction from an embedded login is excluded. The released status-line approach reads documented JSON handed to a local script by Claude Code, with no authenticated network request from Hub. [Authentication guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account).
