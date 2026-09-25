# Technical research and evidence policy

**As of 24 September 2026.** “Verified” means an official page or official open-source client documents the behavior; it does **not** mean we exercised a live account. “Proposed” requires a spike. Absence of a documented interface is recorded as **not found**, not a proof that no private endpoint exists. Source links are attached to each claim and expanded in provider-specific notes.

## Answer to the central question

| Surface | Can existing auth yield current usage and reset without copying a credential? | Decision |
| --- | --- | --- |
| Codex CLI / Codex in ChatGPT Desktop | **Documented structured path:** Codex App Server `account/rateLimits/read` returns one or more quota buckets with `usedPercent`, `windowDurationMins`, `resetsAt`, optional credits and plan. It runs under a Codex context. The command is labeled experimental, so live refresh, profile isolation, and version compatibility need a spike. [App Server](https://learn.chatgpt.com/docs/app-server), [CLI command maturity](https://learn.chatgpt.com/docs/developer-commands) | First connector candidate |
| ChatGPT consumer chat | No documented general per-model subscription usage API found. Codex quota is not a proxy for all ChatGPT chat limits. [App Server scope](https://learn.chatgpt.com/docs/app-server), [ChatGPT usage guidance](https://help.openai.com/en/articles/9824962-openai-o1-o1-mini-and-o3-mini-usage-limits-on-chatgpt-and-the-api) | Detect app; usage unsupported until a supported source exists |
| Claude Code subscription | Official status-line JSON provides 5-hour/7-day used percentages and reset times **only after an API response in an active eligible session**; fields can be absent. `/usage` is interactive. `claude auth status` is JSON but only auth status. No documented standalone quota JSON command found. [Status line](https://code.claude.com/docs/en/statusline), [commands](https://code.claude.com/docs/en/commands), [CLI reference](https://code.claude.com/docs/en/cli-reference) | Optional activity-driven connector, clearly stale when idle |
| Claude.ai / Claude Desktop subscription | Official Settings > Usage displays current session, weekly, model-specific weekly (when included), and credits, but no documented personal usage API or desktop IPC was found. Usage is shared across Claude web/Desktop/Code. [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices), [shared limits](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work) | Detect app; link to official page; no silent extraction |
| OpenAI API organization | Official Usage and Costs endpoints report organization/project/API-key aggregates; separate spend-limit endpoint exists. These require appropriate organization authority, not a ChatGPT subscription session. [Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [spend limit](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/spend_limit/methods/retrieve) | Later privileged connector only |
| Anthropic API organization | Official Usage & Cost Admin API returns historical API tokens/cost, typically within ~5 minutes; admin authority and organization required. Distinct from Claude personal quotas. [Usage & Cost](https://platform.claude.com/docs/en/manage-claude/usage-cost-api) | Later privileged connector only |

## Source priority applied

1. Official structured local interfaces: Codex App Server and Claude `auth status`/status-line contract.
2. Official API: OpenAI and Anthropic organization usage/cost for their **API** surfaces only.
3. Official provider UI: ChatGPT and Claude usage views for manual viewing.
4. Official open-source client: Codex TUI requests `GetAccountRateLimits` from App Server; the lower Codex HTTP header/event parser is an implementation detail, not a third-party API. [TUI source](https://github.com/openai/codex/blob/main/codex-rs/tui/src/app/background_requests.rs), [parser](https://github.com/openai/codex/blob/main/codex-rs/codex-api/src/rate_limits.rs).
5. UI/HTML extraction and private token-backed endpoints: **not selected**. Anthropic explicitly says third-party tools should use API keys and prohibits identity misrepresentation or routing third-party traffic against subscription limits. This is a legal/product review gate, not a technical workaround. [Anthropic authentication guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account).

## Source confidence vocabulary

- **Official documented:** public contract with field names; may still be experimental and change.
- **Official implementation:** source code behavior without a public stability promise.
- **Provider UI:** visible to a user, but no programmatic read contract.
- **Local implementation detail:** file/log schema or IPC inferred from client internals; never assume stable.
- **Unknown:** no verified source. Do not render a fabricated value.

## Distinctions that affect the design

- A Codex quota bucket can be 15 minutes, 5 hours, 7 days, or another duration. Infer its semantic label from returned duration **only when unambiguous**, retain the raw bucket ID, and use a generic title otherwise. [App Server](https://learn.chatgpt.com/docs/app-server).
- API token totals and cost do not establish subscription remaining balance. API spend limits and rate limits are separate from accumulated spend. [OpenAI Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [OpenAI spend guidance](https://help.openai.com/en/articles/6614457-troubleshooting-api-usage-and-spend-limits).
- Claude `rate_limits` status-line fields can be independently absent; its CLI `--output-format json` applies to **model responses in print mode**, not `/usage` output. [Status line](https://code.claude.com/docs/en/statusline), [CLI reference](https://code.claude.com/docs/en/cli-reference).
- Claude Enterprise analytics has distinct organization reporting and lag; it is not a personal subscription quota feed. [Analytics APIs](https://platform.claude.com/docs/en/manage-claude/analytics-api).

## Remaining verification

No live provider account was accessed in this architecture pass. We have not validated whether a fresh `account/rateLimits/read` always returns authoritative values while idle, whether multiple Codex homes can be queried safely in parallel, whether Claude's status-line data is present for every eligible Windows plan/version, or whether ChatGPT/Claude Desktop exposes a supported local read-only usage interface. The [spike plan](15-roadmap.md) defines binary gates.

## Secondary provider outlook (research only)

The contract supports arbitrary provider-defined windows, but no secondary connector is approved or implemented. Gemini CLI documents interactive `/stats model` quota visibility, while Gemini API quotas are per project and use dimensions such as requests/minute, tokens/minute, and requests/day; machine-readable **subscription** quota access still needs a source spike. [Gemini CLI](https://geminicli.com/docs/reference/commands/), [Gemini API](https://ai.google.dev/gemini-api/docs/rate-limits).

GitHub Copilot is a promising future candidate: official Copilot SDK docs describe `account.getQuota` with entitlement, used requests, remaining percentage, and reset date, and GitHub has separate billing usage APIs. Verify account coverage, SDK distribution, and whether calling the SDK reuses an existing local login before designing a connector. [Copilot SDK usage](https://docs.github.com/en/copilot/how-tos/copilot-sdk/features/usage-and-billing), [billing REST API](https://docs.github.com/en/rest/billing/usage).

Cursor documents a **team Admin API** for usage/spend with an admin key, which does not meet the zero-credential personal-context MVP. [Cursor Admin API](https://docs.cursor.com/en/account/teams/admin-api). xAI documents API Usage Explorer and API cost tracking, which are separate from SuperGrok consumer usage. [xAI Usage Explorer](https://docs.x.ai/console/usage), [Grok overview](https://docs.x.ai/grok/overview). For Windsurf and Perplexity, no reliable official personal quota read interface was established in this pass; mark them UNKNOWN and research their current official documentation only when prioritized. Do not infer compatibility from screenshots, extension repositories, or private web calls.
