# Prioritized spikes, MVP, and phased plan

> **Completion update, 25 September 2026:** The personal-use Phase 1 Codex app and Phase 2 Claude Code passive integration are implemented. The table below is the original research roadmap, not pending phases. See [Phase 2 implementation](18-phase2-implementation.md).

> **Phase numbering update (24 September 2026):** At the user's direction, implementation **Phase 1** is the complete usable OpenAI/Codex Windows app. The original staged table below is preserved as research history, not the current delivery sequence. **Phase 2** is Claude. See [Phase 1 implementation](17-phase1-implementation.md).

## Binary Phase 0 spikes

Run on a clean Windows 11 profile and at least two real pre-authenticated test contexts where possible. Use read-only provider interfaces; do not inspect token files or send model prompts.

| Priority | Spike | YES criterion | NO criterion / outcome |
| --- | --- | --- | --- |
| 1 | Codex limits via App Server | Two separate existing `CODEX_HOME` roots return correct identity and nonempty structured quota buckets/reset timestamps on demand, repeatedly, without switching the user's default context; compare with near-contemporaneous `/status` and record any drift | Missing or persistently stale fields, account leakage, required login, or fragile version behavior → no auto Codex connector; release gate blocks quota claims |
| 2 | Claude subscription limits | Existing Claude Code status-line feed emits 5h/7d percentage + reset for two independent `CLAUDE_CONFIG_DIR` contexts after normal activity, binds correctly, and stale policy behaves; document that it cannot independently poll | Feed absent/ambiguous → identity-only/manual official Usage page; do not scrape or use private OAuth endpoint |
| 3 | Multi-context coexistence | Two Codex homes and two Claude roots can be probed concurrently/sequentially with stable identities and no mutation of another app's active account | Alias/roots switch provider account or collide → limit supported account count to independently safe contexts |
| 4 | WPF notch/HWND | No focus theft, correct top-center geometry on mixed DPI/multi-monitor, auto-hide above fullscreen, recovery after sleep/disconnect/taskbar change | Repeated focus/fullscreen failure → alter HWND strategy or reevaluate WinUI/Win32 shell |
| 5 | Local data boundary | Instrumented Hub never reads/copies provider auth files or puts secrets in SQLite/logs; bridge uses current-user ACL and no network listener | Any secret propagation → stop connector release and redesign |
| 6 | Packaging | Signed MSIX installs, starts at login, notifies, updates and uninstalls without breaking tray/notch or provider homes | MSIX limitation → signed unpackaged installer path |

Record per-spike: provider CLI version, Windows build, test accounts' safe aliases, source fields, timing, side effects, and decision. Do not store raw auth output. **Spike 1 is the first code task**: a small throwaway read-only probe of Codex App Server JSONL, not the full application.

## Smallest useful MVP after gates

One notch + minimal Overview/Accounts/Alerts/Settings app; zero-provider-credential discovery; multiple **existing** Codex and Claude Code context bindings; Codex quota connector only if Spike 1 passes; optional Claude Code passive feed only if Spike 2 passes; explicit unavailable state and link to official Claude Usage page otherwise; normalized arbitrary windows; conservative scheduler; local account/window alerts; SQLite; tray/startup; signed installer. No ChatGPT consumer chat quota claim, Claude Desktop IPC claim, API admin connector, provider login, WebView, or HTML scraping. A single reliable Codex connector can ship a useful beta while Claude is manual/status-only.

## Delivery phases

| Phase | Objective | Tasks | Dependencies | Acceptance | Tests | Risk |
| --- | --- | --- | --- | --- | --- | --- |
| 0 Research/spike | Prove safe data access and notch feasibility | Revalidate sources; tiny read-only Codex probe; Claude feed, context, HWND, data-boundary, packaging experiments | Existing test contexts and provider CLIs | Explicit YES/NO records for six spikes; no secrets copied | Live read-only probes, focus/packaging harness, secret canaries | Codex command experimental; Claude cannot independently poll |
| 1 Shell + notch | Establish lightweight Windows utility | WPF/tray process; collapsed/expanded states; monitor/fullscreen policy; simple full app shell | Spike 4 | Visible, responsive, non-disruptive notch and four-page shell | Hardware matrix for focus, DPI, monitor, fullscreen, wake | Topmost interference |
| 2 Core/data | Preserve source truth | Domain records; SQLite migrations; snapshot/freshness/alert contracts | Phase 1 view binding | Arbitrary windows and unknown values round-trip without invention | Null/unknown fixture tests and 30-account performance | False equivalence between quotas |
| 3 First real connector | Read Codex quotas | App Server adapter; discovery/probe; version gate; scheduler | Spikes 1/3/5; Phase 2 | Live read-only quota/reset agrees with near-contemporaneous Codex display; no auth mutation | Fake CLI schema tests and live version matrix | CLI protocol drift |
| 4 Multiple accounts | Bind existing contexts independently | Context registry; path registration; per-child env; dedupe/conflict UX | Phase 3 | Two distinct Codex roots query independently; unverified contexts never count as accounts | Identity-change and concurrent-context tests | Custom roots are not enumerable |
| 5 Second provider | Add honest Claude visibility | Claude auth probe; opt-in status-line feed or identity-only state | Spikes 2/3/5 | Correct scoped windows when emitted; stale labels when idle | Missing-field, multi-context, inactivity tests | Feed absence and terms |
| 6 Alerts/refresh | Notify without noise | Conservative polling; backoff; local notifications; reset handling | Phases 3–5 | Account/window-specific alerts, no stale alerts | Crossing, cooldown, reset, offline recovery tests | Alert noise |
| 7 Installer/security | Prepare trustworthy release | Sign; package/update/startup; redaction; migration; abuse tests | Spike 6, phases 1–6 | Install/update/uninstall without provider state changes | Clean-profile release matrix and zero-secret canaries | MSIX behavior and signing |
| 8 Beta | Validate real-world reliability | Limited Windows 11 beta; collect opt-in diagnostics; update docs | All gates | No false quota claims; idle resources meet measured target set in beta plan | Long-run CPU/memory, provider version drift, support triage | Provider changes during beta |

Reassess MVP if Codex App Server fails: ship an honest account/status utility only if it still solves a user job; otherwise pause product implementation rather than manufacture quota figures.
