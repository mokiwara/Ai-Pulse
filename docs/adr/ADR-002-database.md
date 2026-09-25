# ADR-002: SQLite for local metadata and snapshots

**Status:** accepted provisionally. **Date:** 24 September 2026.

## Decision

Use one per-user SQLite database under the app's local data directory for accounts, existing-context references, normalized snapshots/windows, alerts, settings, and bounded refresh history. Use migrations, foreign keys, WAL, short transactions, and a retention job. One writer is sufficient. No cloud database or sync in MVP.

## Rationale

The data is relational and small, with account-window and alert-rule queries. SQLite is widely supported in .NET, allows transactional migration and offline use, and avoids a service. JSON files would complicate concurrent updates/migrations and consistency; an external DB would violate the simple local-first deployment. The database is **not encrypted credential storage**. Provider secrets stay with provider CLIs and never enter Hub. If later confidentiality requirements demand at-rest encryption of usage metadata, evaluate it separately, with key lifecycle and Windows user protection specified. See [data model](../05-data-model.md) and [security](../09-security.md).

## Consequences

Back up before schema upgrades; handle corruption with user-visible repair/export of safe metadata. Keep last useful samples but prune history. Do not retain raw provider JSON. Sharing one DB across Windows users is unsupported; each SID gets a separate installation context.
