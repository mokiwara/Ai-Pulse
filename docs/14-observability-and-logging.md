# Observability and logging

> **Phase 1 implementation:** The app logs only allowlisted error codes and context UUIDs to `hub.log`, rotates around 1 MB, and prunes files older than 14 days. It does not log provider stdout/stderr, paths, email, or environment values. The richer diagnostic/export plan below is future work. See [implementation record](17-phase1-implementation.md).

The product is local-first with no telemetry by default. Keep a small structured local diagnostic ring for connector health and UI lifecycle. Default retention: 14 days or 10 MB, whichever arrives first; user can clear it. Exports are opt-in, previewable, and redact account labels/emails/paths unless explicitly included.

## Safe event schema

`timestamp`, `eventCode`, `connectorId`, `connectorVersion`, anonymized context UUID, outcome category, duration, retry delay, safe provider request ID, and field-presence bitmap. Never log raw CLI stdout/stderr, request/response bodies, environment variables, credentials, browser state, provider conversation text, or full file paths. Normalize errors to codes such as `AUTH_REQUIRED`, `SCHEMA_CHANGED`, `TIMEOUT`, `RATE_LIMITED`, `OFFLINE`, `PROVIDER_ERROR`.

Run a redaction layer before persistence **and** before export; use an allowlist rather than trusting regex alone. Test with fake secret canaries. Source confidence, fetchedAt, and stale status are visible in account details so a user can diagnose whether a number is current without reading logs.

## Health UI

Accounts page shows installed tool/version, context alias, identity verification, quota availability, last success, last attempt, next retry, and actionable error. A local “copy diagnostics” command emits redacted summary only. No support upload endpoint in MVP. Connector crashes do not crash the notch process; exceptions are isolated and convert to stale/error state.
