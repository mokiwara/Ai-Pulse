# Security strategy and threat model

## Zero-provider-credential MVP

AI Usage Hub stores **no provider password, API key, OAuth token, refresh token, session cookie, or copy of CLI authentication files**. The provider-owned CLI authenticates itself. Hub invokes documented read-only methods and consumes allowlisted output. A local path to an existing CLI config root is a pointer, not a secret. Do not create a provider login flow. Do not call provider logout during Hub disconnect. [Codex auth storage](https://learn.chatgpt.com/docs/auth), [Claude auth storage](https://code.claude.com/docs/en/authentication).

| Location | Contents |
| --- | --- |
| SQLite | account aliases, verified non-secret identity, context path/ID, latest normalized usage windows, alert/settings and bounded refresh history |
| Windows Credential Manager / DPAPI | **Nothing in MVP**. Reserve an abstraction for a future opt-in privileged API connector; separate ADR required before use. |
| Memory only | transient CLI stdout JSON, process handles, temporary derived view state; clear after normalize |
| Provider-owned store | Codex/Claude credentials, completely outside Hub ownership |

Provider stores may themselves be files with tokens (for example, Codex `auth.json` or Claude `.credentials.json`). Hub must not open, back up, hash contents, watch contents, or include them in diagnostics. The CLI may legitimately read its own file. Metadata-only discovery uses directory existence and `auth status`/`account/read` via the provider executable. [OpenAI authentication](https://learn.chatgpt.com/docs/auth), [Claude authentication](https://code.claude.com/docs/en/authentication).

## Threats and controls

| Threat | Control | Residual risk |
| --- | --- | --- |
| Malicious local process under same Windows user | Per-user mutex/IPC ACL, no network listener, no exported secrets, code signing | Same-user malware can often access the provider's own auth and local UI; Hub cannot defend against full user compromise |
| Stolen SQLite | No credentials; restrict file ACL to current SID; optionally encrypt sensitive account labels under DPAPI later | Usage/identity metadata remains sensitive |
| Support logs uploaded | Structured allowlist, redact token patterns, paths/emails off by default, explicit preview/export | Provider stderr can contain unexpected secrets; never log raw stdout/stderr |
| Compromised connector | First-party compiled registry, no dynamic assembly loading, argv/environment allowlist, separate child process, no credential-file access | A malicious Hub binary under same user can still read provider data; code signing and release pipeline matter |
| Accidental token exposure | Ban auth file reads and raw HTTP to private subscription endpoints; inspect output schema and redact before persistence/logging | CLI implementation can change output; version gate and regression tests |
| Account confusion | Bind source to context UUID and verified native account ID; detect identity change and quarantine old snapshot/alerts | Some provider identity fields may be absent; mark unresolved |
| Local bridge spoofing (Claude status-line) | Named pipe or local file channel with current-user ACL, nonce/handshake, schema validation, monotonic timestamps, explicit context binding | Same-user malicious process may spoof data; display source confidence |

No web server or cloud backend in MVP. Network requests occur only inside provider-owned CLI calls. A future opt-in API connector adds direct network egress and a new credential ADR/security review. Do not intercept TLS or inject into provider processes.

## Operational rules

Use absolute executable paths, publisher/version check where possible, argv arrays, child process timeouts, output-size caps, and strict JSON parsing. Never launch through a shell. Do not pass secrets in arguments or environment. Do not install status-line hooks without showing the exact change and preserving any existing script. No telemetry by default. See [observability](14-observability-and-logging.md).
