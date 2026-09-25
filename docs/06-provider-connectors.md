# Provider connector system

> **Implemented through Phase 2:** Codex remains an active App Server poller. Claude Code is an event-driven status-line feed plus a bounded `claude auth status` probe. The released app preserves existing custom status lines, configures an empty one for monitored contexts, and checks its local feed every 30 seconds. Claude Desktop has no supported quota connector. The contract and plugin sections below are design background. See [Phase 2 record](18-phase2-implementation.md).

## Connector contract

A connector describes a **data source**, not just a brand. MVP adapters are `CodexAppServerConnector`, `ClaudeCodeStatusLineConnector`, plus read-only detectors for ChatGPT Desktop and Claude Desktop. OpenAI API and Anthropic API connectors are specified as later privileged modules, disabled in MVP.

Conceptual asynchronous contract (implementation names can evolve):

1. `Describe()` returns connector ID/version, provider/surface, source classification, supported auth-context types, capabilities, refresh recommendation, and minimum CLI version.
2. `DiscoverCandidates(scanScope)` returns known executable/default-context candidates without reading credential contents or enumerating arbitrary user directories.
3. `Probe(context)` invokes safe provider-owned identity/status methods and returns verified identity, source mode, availability, and diagnostic category.
4. `ReadUsage(context, cancellation)` returns a normalized immutable snapshot or typed unavailable/error result.
5. `DisconnectBinding(context)` removes only AI Usage Hub's reference/bridge; it must **never** call provider logout or delete provider state.

No `Authenticate()` in the MVP contract. The provider CLI or desktop app already owns sign-in. `TestConnection` is `Probe`; account discovery and usage querying are distinct so “installed” is not confused with “connected.” [ADR-003](adr/ADR-003-provider-connector-model.md).

## Capability model

Separate **field** capabilities (`UsageWindows`, `ResetTimes`, `AbsoluteLimits`, `Percentages`, `Credits`, `Billing`, `Costs`, `Identity`, `Plan`) from **operational** capabilities (`AutomaticRefresh`, `OnDemandRefresh`, `ActivityFeed`, `MultipleIndependentContexts`, `SupportedMachineOutput`, `RequiresAdminAuthority`). `AuthContextKind` is descriptive (`ExistingCli`, `ExistingDesktop`, `AdminApi`, `BrowserSessionFuture`); it never triggers Hub login. Values are `Supported`, `Conditional`, `Unsupported`, or `Unknown`, with reason and source URL. An API-auth capability may be declared for a future privileged connector, while browser auth is excluded from MVP. A connector can change capabilities per context and version.

## Source adapters and safety

| Adapter | Safe read path | Refresh | Limits |
| --- | --- | --- | --- |
| Codex App Server | Spawn already-installed `codex app-server` with inherited or explicitly selected `CODEX_HOME`; JSONL `initialize` + `account/read` + `account/rateLimits/read`; terminate child | Pull at conservative interval, or on manual request | Command is experimental; verify version and source freshness. No login/logout or model turn. [App Server](https://learn.chatgpt.com/docs/app-server) |
| Claude Code status line | Optional user-enabled status-line command receives official JSON stdin and emits only allowlisted quota fields into a local AI Usage Hub bridge | Event-driven; stale on inactivity | First response required; Pro/Max documented; may be absent. Never replace the user's existing status line silently; compose with permission and preserve output. [Status line](https://code.claude.com/docs/en/statusline) |
| Claude Code auth probe | `claude auth status` JSON with explicit `CLAUDE_CONFIG_DIR` context | Discovery/manual | Auth state only, **no quota**. [CLI reference](https://code.claude.com/docs/en/cli-reference) |
| ChatGPT/Claude Desktop detector | Installed-app inventory and official link | On scan | Installation is not auth or usage evidence. No private IPC or process injection. |

Run executables by canonical absolute path and argv array, never through `cmd.exe` or shell interpolation. Validate executable publisher and version before first run where feasible. Cap stdout, reject unexpected JSON shape, apply timeouts and cancellation, and scrub stderr before logging. Avoid invoking `claude -p` to ask for usage: it runs a model turn and may consume quota. `codex exec --json` is a model task stream, not a documented quota query. [Claude CLI](https://code.claude.com/docs/en/cli-reference), [Codex App Server](https://learn.chatgpt.com/docs/app-server).

## Future plugin boundary

Do not load arbitrary .NET assemblies into the main process. For later third-party connectors, define a signed manifest with provider/surface/source, schema version, permissions, executable allowlist, and a subprocess JSON protocol. Grant each plugin only its declared context paths and a narrow result channel. This is future architecture, not a claim that provider integrations exist.
