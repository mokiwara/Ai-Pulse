# Provider capability matrix

> **Implementation update, 25 September 2026:** Codex App Server and Claude Code status-line JSON are implemented. The Claude feed is passive and was not verified against live subscription usage because the local Claude Code context is logged out. Claude Desktop and organization APIs have no Hub connector. See [Phase 2 record](18-phase2-implementation.md).

**As of 24 September 2026.** YES = documented for that source; PARTIAL = conditional, stale, or only a subset; NO = documented absence/scope exclusion; UNKNOWN = not established. `5h` and `weekly` refer to quota windows, never aggregate API reports. Reset time is a provider-supplied or documented page value, not a calculated guess.

| Provider | Product / source | 5h | Weekly | Reset | Multi-account | Auth context | Reliability | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OpenAI | ChatGPT chat / official UI | UNKNOWN | PARTIAL | PARTIAL | UNKNOWN | Existing browser/app | Provider UI | Model limits vary; no general usage API found; some limits show reset in selector, usage count may be unavailable. [Help](https://help.openai.com/en/articles/9824962-openai-o1-o1-mini-and-o3-mini-usage-limits-on-chatgpt-and-the-api) |
| OpenAI | Codex CLI/App Server | PARTIAL | PARTIAL | YES | PARTIAL | Existing Codex login per `CODEX_HOME` | Official documented, experimental command | Structured quota buckets; duration and secondary bucket are runtime data, never guaranteed 5h/weekly. [App Server](https://learn.chatgpt.com/docs/app-server) |
| OpenAI | ChatGPT Desktop Codex agent | PARTIAL | PARTIAL | PARTIAL | PARTIAL | Shared native Codex home by default | Official docs + spike needed | Native Windows app shares `%USERPROFILE%\.codex`; use Codex interface, not desktop IPC. [Windows app](https://learn.chatgpt.com/docs/windows/windows-app) |
| OpenAI | API organization Usage/Costs | NO | NO | NO | PARTIAL | Admin/organization API credential | Official stable API | Token/request buckets and cost, separate spend limits. Not subscription usage. [Usage API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage) |
| Anthropic | Claude.ai subscription / Settings > Usage | YES | YES | YES | UNKNOWN | Existing website session | Provider UI | Includes weekly model-specific window if plan has one; no documented machine API. [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices) |
| Anthropic | Claude Desktop | YES | YES | YES | UNKNOWN | Existing desktop session | Provider UI | Same subscription limits; no supported local IPC established. [Shared limits](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work) |
| Anthropic | Claude Code status-line JSON | PARTIAL | PARTIAL | PARTIAL | PARTIAL | Existing `CLAUDE_CONFIG_DIR` | Official documented, activity-driven | Pro/Max only per docs, after first response; each field may be absent or stale. [Status line](https://code.claude.com/docs/en/statusline) |
| Anthropic | API Usage & Cost Admin API | NO | NO | NO | PARTIAL | Admin authority | Official stable API | Historical API token/cost reports, separate from subscription quota. [Usage & Cost](https://platform.claude.com/docs/en/manage-claude/usage-cost-api) |

### Field-level availability

| Source | Used | Remaining | Maximum | Percentage | Reset | Plan | Identity | Cost/credits |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Codex App Server | PARTIAL: percent | UNKNOWN | UNKNOWN | YES | YES when returned | PARTIAL | YES via `account/read` | PARTIAL credits; token activity via separate `account/usage/read` |
| Claude Code status-line | PARTIAL: percent | UNKNOWN | UNKNOWN | YES | YES when field exists | UNKNOWN | PARTIAL via `auth status` | Session cost is an estimate, not subscription balance |
| Claude Settings > Usage | PARTIAL: progress | PARTIAL | UNKNOWN | YES | YES | PARTIAL | Signed-in account | Credits/extra usage shown when enabled |
| OpenAI org API | YES token/request/cost | Separate spend-limit read | Separate spend-limit read | Calculated only when same-scope limit verified | Bucket end, not necessarily quota reset | API tier elsewhere | Org/project/key IDs | YES costs |
| Anthropic org API | YES token/cost | Separate limit sources | Separate limit sources | Calculated only for verified same-scope cap | Bucket end, not subscription reset | UNKNOWN | Org/workspace/key IDs | YES costs |

**Multi-account interpretation:** An AI Usage Hub account row is independently queryable only when a separate provider context actually exists. Codex `--profile` changes configuration layers, not proven login identity. Claude `CLAUDE_CONFIG_DIR` is documented for side-by-side accounts, but arbitrary directories cannot be safely enumerated. Details: [11-multi-account-auth](11-multi-account-auth.md).

### Connector operation and risk

| Source | Auth method | Refresh method | Multi-account rating | Legal/terms risk | Limitations |
| --- | --- | --- | --- | --- | --- |
| Codex App Server | Existing Codex-managed login | Local CLI child, JSONL RPC | Possible via known separate `CODEX_HOME` roots | Low for documented read; investigate experimental-command stability | No credential copy; test each root and version. [App Server](https://learn.chatgpt.com/docs/app-server) |
| ChatGPT consumer UI | Existing app/browser session | Manual official UI | Difficult/unknown | Low for manual viewing; avoid extraction | No machine read contract found. [Help](https://help.openai.com/en/articles/9824962-openai-o1-o1-mini-and-o3-mini-usage-limits-on-chatgpt-and-the-api) |
| Claude Code status line | Existing Claude Code login | Event-driven local JSON feed | Possible via known `CLAUDE_CONFIG_DIR` roots | Investigate distribution/integration policy | Optional opt-in; no fresh polling. [Status line](https://code.claude.com/docs/en/statusline) |
| Claude subscription Usage page | Existing browser/Desktop session | Manual official UI | Difficult/unknown | Low for manual viewing; **avoid** token/HTML extraction | No supported auto-read established. [Usage settings](https://support.claude.com/en/articles/9797557-usage-limit-best-practices), [third-party guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account) |
| OpenAI/Anthropic organization APIs | Admin-scoped API authority | REST polling | Excellent for distinct organizations/projects once authorized | Low for official API, outside zero-credential MVP | API usage only; privileged key/identity and product-specific scope. [OpenAI](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [Anthropic](https://platform.claude.com/docs/en/manage-claude/usage-cost-api) |
