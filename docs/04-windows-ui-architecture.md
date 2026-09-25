# Windows UI and notch architecture

> **Implemented Phase 1:** The full app uses Overview, Accounts, and Settings; alerts are in Settings. The notch uses one non-activating HWND and display selection, with a fullscreen heuristic. Mixed-DPI and multi-monitor hardware coverage remains to be verified. See [implementation record](17-phase1-implementation.md).

## Window composition

Create one small borderless WPF top-level HWND for the notch and a normal WPF main window. The notch uses `WindowStyle=None`, `ShowInTaskbar=false`, transparent per-pixel WPF rendering, and a bounded click target. Set `ShowActivated=false`; after `SourceInitialized`, inspect/set extended styles such as `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW`, then position with `SetWindowPos(..., HWND_TOPMOST, ..., SWP_NOACTIVATE)`. These are documented HWND controls, but their interaction with WPF popups and keyboard access needs Spike 4. [WPF ShowActivated](https://learn.microsoft.com/dotnet/api/system.windows.window.showactivated), [extended styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles), [SetWindowPos](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos).

Use **one notch HWND** that resizes on expansion; avoid separate topmost popup windows and focus fights. Closed notch is non-activating. Pointer activation may open a regular settings window, which can take focus intentionally. Expanded panel supports keyboard access when explicitly invoked via tray or shortcut; test whether the no-activate style should be temporarily removed for keyboard navigation. Do not apply `WS_EX_TRANSPARENT` to the whole interactive notch: it passes clicks through. Only toggle click-through for a completely hidden/visual-only state if testing justifies it. [Win32 window features](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).

## Geometry and system changes

- Anchor to the **selected monitor's physical display bounds**, top center; monitor preference: user pin > active monitor > primary monitor. Clamp the expanded panel to usable visible area. Store monitor device ID and gracefully fall back after disconnect.
- Calculate in physical pixels using the HWND's monitor DPI; convert to WPF device-independent units only at the boundary. React to `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, taskbar/appbar changes, session lock/unlock, and resume. Debounce geometry recomputation.
- Do not reserve work area or register as an appbar. A topmost notch overlaps pixels; minimize interference by keeping it small and hiding over exclusive fullscreen or user-configured app lists. Borderless fullscreen detection is heuristic, so test with games, video, PowerPoint, remote desktop, and multiple monitor layouts.
- Avoid focus stealing on refresh or notification. Do not hijack Windows snapping; the normal main window snaps normally. Reopen the panel inside display bounds after resolution/taskbar changes.
- Respect reduced motion and high contrast; use DWM/WPF translucency only when composition is available. Fall back to opaque theme. Pause animation while collapsed, hidden, locked, or under battery saver.

## Notch information policy

Normal: icon + account count + quiet health dots. Warning: highest-severity **fresh** account/window above threshold. Reset-soon: one fresh window with a known reset timestamp. Tie-break by severity, then percent, then user pin. A stale value never wins a warning slot; show `Updated 8m ago` or a stale glyph. Distinguish account identity from product surface (`OpenAI • Work • Codex weekly`). Do not infer absolute message counts from a percentage.

Expanded panel: provider headers, per-account windows in source order, reset timestamp and source freshness, then Open App and Settings. Preserve simple interaction for 2–6 accounts. Full app: three top-level pages (Overview, Accounts, Settings), with alerts in Settings. The three root inspiration PNGs define visual direction but contain fabricated examples, prices, and providers outside MVP.

## Tray, notifications, startup

The tray icon remains available when the notch auto-hides. A per-user startup registration launches the app after login. Use Windows app notifications where packaging allows; notification click opens the exact account/window. Code signing and MSIX/App Installer are the preferred release path, subject to the packaging spike. [Windows packaging](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/packaging/), [App Installer updates](https://learn.microsoft.com/en-us/windows/msix/app-installer/auto-update-and-repair--overview).
