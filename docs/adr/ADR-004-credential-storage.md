# ADR-004: No Hub-owned provider credentials in MVP

**Status:** accepted. **Date:** 24 September 2026.

## Decision

AI Usage Hub stores no provider credentials. Query already-authenticated provider CLI contexts and parse safe structured output. Store only local context references and normalized non-secret account/usage metadata in SQLite. Do not call provider private endpoints using extracted tokens. Do not read Codex `auth.json` or Claude `.credentials.json`. [Codex storage](https://learn.chatgpt.com/docs/auth), [Claude storage](https://code.claude.com/docs/en/authentication).

## Alternatives for future opt-in connectors

If a future official API connector requires a key, the preferred storage candidate is Windows Credential Locker/Manager with a reference ID in SQLite; DPAPI `CurrentUser` is another option for encrypted fields. Neither prevents a malicious process already running as the same user from using the secret. The precise API, scope, migration, deletion, and backup behavior require a **new ADR and threat review** before implementation. [Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker), [DPAPI](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata).

## Consequences

OpenAI and Anthropic organization usage APIs are out of the zero-credential MVP unless a documented already-authenticated local command becomes available. This is an intentional product limit, not permission to prompt for a subscription API key. [Security strategy](../09-security.md).
