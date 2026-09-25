# Multiple existing accounts and discovery

## Identity versus context

An account is a provider identity. A **context** is a provider-owned place where one identity can be queried without switching a global login: a Codex home, Claude Code config directory, or a future documented profile. The same identity can appear through more than one context. AI Usage Hub registers many contexts and checks each independently; it never signs in, logs out, writes provider auth state, or silently changes the account active in another app.

| Surface | Simultaneous contexts | Local representation | Enumerate all? | Safe independent query? | Disruption risk |
| --- | --- | --- | --- | --- | --- |
| Codex CLI | Possible through separate `CODEX_HOME` roots; live test needed | Default `%USERPROFILE%\.codex`; environment override | **No official all-context enumerator** | Proposed App Server child per known root | Low if read-only and root-specific; verify in spike. `--profile` is config, not login. [Windows app](https://learn.chatgpt.com/docs/windows/windows-app), [App Server](https://learn.chatgpt.com/docs/app-server) |
| ChatGPT Desktop | Default native agent shares Codex home | Installed app + shared default home | Other desktop identities UNKNOWN | Codex quota via same root only | Do not attach to or switch desktop process. [Windows app](https://learn.chatgpt.com/docs/windows/windows-app) |
| Claude Code | **Documented** with `CLAUDE_CONFIG_DIR` | Default `%USERPROFILE%\.claude`, custom roots | **No official all-context enumerator** | `claude auth status` per root; status-line feed per active session | Low with explicit env and no login/logout; active feed integration requires opt-in. [Env vars](https://code.claude.com/docs/en/env-vars), [CLI reference](https://code.claude.com/docs/en/cli-reference) |
| Claude Desktop | Not established | Desktop-owned session | UNKNOWN | No documented local quota IPC found | Do not inspect or alter desktop state. [Shared limits](https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work) |
| OpenAI/Anthropic API | Organization/project keys may coexist | Provider API scopes, not subscription contexts | Out of MVP | Needs privileged opt-in | No credential reuse assumed |

## Discovery UX and algorithm

First launch shows **Detected tools** with separate labels: Installed, Existing context found, Identity verified, Usage available. Scan `PATH` and trusted standard installation locations for Codex/Claude executables; validate canonical executable and version. Check default config directories only for existence, then call `codex login status` or Codex App Server `account/read`, and `claude auth status` with explicit `CLAUDE_CONFIG_DIR`. Also inspect current process environment for context-root hints. Do not recursively search drives, inspect token files, or claim an account based solely on a directory.

For additional Personal/Work/Client contexts, offer **Add existing context path**. The user selects a root already used by their CLI. Validate the directory is within a user-allowed location, run a read-only probe, show returned identity/auth mode, then bind it. The app never creates a second login. If a root is not authenticated, show “Open this context in Codex/Claude to sign in” as a provider-owned action, without invoking login itself.

Deduplicate by provider-issued stable ID if the safe output supplies it; email alone is insufficient for cross-workspace identity. If only email/plan is returned, retain context-specific rows until the user explicitly merges them. Display a conflict if one context's identity changes and quarantine its old quota/alerts. Probe all contexts without mutating the default environment; spawn child processes with a copy of environment changed only for that child. Avoid parallel probes of the **same** context until the provider confirms safe concurrency.

## Practical limits

Three separate account rows require three existing independently authenticated contexts. The app cannot enumerate arbitrary CLI homes created by shell aliases, WSL distros, Windows user profiles, or desktop account switching. WSL can be added later only with explicit distro/context opt-in and a safe command bridge. Separate Windows users are intentionally isolated by OS boundary; do not reach across profiles. [Codex Windows/WSL](https://learn.chatgpt.com/docs/windows/windows-app).
