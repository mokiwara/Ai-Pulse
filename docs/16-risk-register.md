# Risk register

| ID | Risk | Likelihood / impact | Mitigation and gate |
| --- | --- | --- | --- |
| R1 | Codex App Server command is documented but experimental and may change | High / High | Version gate, source fixtures, live Spike 1, fail closed on schema drift. [CLI maturity](https://learn.chatgpt.com/docs/developer-commands) |
| R2 | Codex rate-limit buckets may omit expected 5h/weekly or reset | Medium / High | Dynamic windows; never invent types; show only returned buckets. [App Server](https://learn.chatgpt.com/docs/app-server) |
| R3 | Claude Code status-line data absent or stale while idle | High / High | Optional activity feed, source timestamp, stale after configured TTL, official Usage page for manual check. [Status-line conditions](https://code.claude.com/docs/en/statusline) |
| R4 | Claude private endpoint or browser extraction violates terms/changes | High / High | Exclude from MVP; legal/provider review before any future fallback. [Anthropic guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account) |
| R5 | Multiple accounts not enumerable or same underlying account repeated | High / Medium | Default + explicit existing context paths; identity probe; no promised count before verification. [Claude contexts](https://code.claude.com/docs/en/env-vars), [Codex home](https://learn.chatgpt.com/docs/windows/windows-app) |
| R6 | API billing confused with subscriptions | Medium / High | Separate ProductSurface and connector; API admin modules outside MVP. [OpenAI API](https://developers.openai.com/api/reference/resources/admin/subresources/organization/subresources/usage), [Anthropic API](https://platform.claude.com/docs/en/manage-claude/usage-cost-api) |
| R7 | Notch steals focus or covers fullscreen content | Medium / High | HWND spike, explicit hide policy, non-activating placement, user override. [Window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles) |
| R8 | CLI child or local bridge leaks credentials in diagnostics | Medium / High | No credential reads, allowlisted stdout, scrubbed structured logs, canary test gate |
| R9 | Provider executable substitution or compromised status-line helper | Medium / High | Absolute path/publisher checks, ACLs, signed releases, no shell, explicit opt-in |
| R10 | MSIX update/startup/tray constraint | Medium / Medium | Packaging spike; signed unpackaged fallback. [MSIX](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/) |
| R11 | Main app becomes a dashboard instead of a utility | Medium / Medium | Four-page navigation, no historical chart in MVP, usability review with 2 and 30 accounts |
| R12 | September 2026 provider docs or terms change before shipment | High / High | Revalidate linked official sources during Phase 0 and each connector release |

Owner for R1–R6 is connector lead; R7/R10 Windows lead; R8/R9 security reviewer; R11 product lead. No risk is considered closed solely by this design document.
