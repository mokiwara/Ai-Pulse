# ADR-005: No embedded provider authentication in MVP

**Status:** accepted. **Date:** 24 September 2026.

## Decision

Do not embed provider login pages, use WebView as a subscription authenticator, copy browser cookies, inspect desktop web sessions, or scrape authenticated HTML in the MVP. Offer a button that opens the provider's official Usage page in the user's existing browser for manual inspection when automatic data is unavailable. AI Usage Hub remains a consumer of existing provider-owned authenticated tools.

## Future fallback conditions

Only reconsider an isolated WebView if a provider permits the flow, there is no better official local/CLI/API source, the user explicitly chooses it, and security/legal review approves it. WebView2 technically supports isolated profiles, but Microsoft warns against extracting tokens from embedded OAuth login pages; profile isolation is a mechanism, not provider authorization. If ever used, bind one profile per account, do not export cookies, and preserve account identity boundaries. [WebView2 profiles](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/multi-profile-support), [Microsoft OAuth warning](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2), [Anthropic guidance](https://support.claude.com/en/articles/13189465-log-in-to-your-claude-account).

## Consequences

Some consumer subscription usage remains unavailable in Hub. Show `Open official Usage page` and an honest status; never substitute API usage or an inferred quota.
