# ADR-003: Connector model follows data sources and existing contexts

**Status:** accepted. **Date:** 24 September 2026.

## Decision

Use first-party compiled adapters implementing `Describe`, `DiscoverCandidates`, `Probe`, `ReadUsage`, and `DisconnectBinding`. A connector reports capability by field and by refresh mode. An account can bind multiple existing contexts. No generic provider authentication method exists in MVP. A connector's read result is either a normalized snapshot or a typed unavailable/error result.

## Why

Codex has a documented local structured read through its CLI App Server; Claude Code has an activity-driven status-line feed; their contracts and freshness differ. A single provider-level `SupportsUsageWindows=true` flag would imply more than either source guarantees. Separate source adapters allow future API/admin surfaces without conflating subscription limits. [Codex App Server](https://learn.chatgpt.com/docs/app-server), [Claude status line](https://code.claude.com/docs/en/statusline).

## Consequences

The scheduler consumes connector-specific refresh recommendations. Version and source confidence travel with every snapshot/window. Plugin extensibility begins with internal registry and explicit contracts; arbitrary third-party assembly loading is deferred. Disconnect removes Hub bindings only, never provider login. [Connector design](../06-provider-connectors.md).
