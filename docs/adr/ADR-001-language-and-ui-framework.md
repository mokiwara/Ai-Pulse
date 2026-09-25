# ADR-001: Language and Windows UI framework

**Status:** accepted provisionally, subject to HWND Spike 4. **Date:** 24 September 2026.

## Context

The hard part is a small non-activating, transparent topmost Windows HWND that behaves across DPI, fullscreen, and monitors. The settings app is simple. The provider interfaces are local processes and JSON, so none of the compared UI frameworks gains a unique connector advantage.

| Option | Strengths | Costs for this product | Decision |
| --- | --- | --- | --- |
| C#/.NET 10 + WPF | Direct WPF HWND access, mature XAML data binding/animation, native Windows APIs and tray ecosystem; .NET 10 current WPF release. [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/), [.NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100) | Need custom Fluent styling and Win32 interop; WPF composition/transparency testing | **Choose** |
| C#/.NET 10 + WinUI 3 | Microsoft's recommended framework for new Windows apps; AppWindow modern windowing and native visual language. [Windows path](https://learn.microsoft.com/en-us/windows/apps/get-started/), [windowing](https://learn.microsoft.com/en-us/windows/apps/develop/ui/windowing-overview) | Notch still requires precise HWND behavior; app lifecycle/runtime adds packaging complexity for small utility | Revisit if WPF spike fails |
| Rust + Tauri | Rust backend, web UI, Windows WebView2, custom windows. [Tauri prerequisites](https://v2.tauri.app/start/prerequisites/) | Adds web frontend and WebView process for a tiny always-on UI; HWND edge cases still need Windows-specific code | Defer |
| Python + PySide6 | Rapid prototype; Qt frameless windows. [Qt flags](https://doc.qt.io/qtforpython-6/PySide6/QtCore/Qt.html) | Distribution/runtime footprint and Windows-specific focus/overlay interop remain; less straightforward signed native packaging | Spike-only option |
| Electron | BrowserWindow has transparent/focus/tray facilities and mature web UI. [BrowserWindow](https://www.electronjs.org/docs/latest/api/browser-window) | Chromium process overhead and broader JS/native bridge for an idle utility; still must test fullscreen/focus | Defer |
| C++/Win32 | Maximum HWND control | UI and connector delivery cost high for small app | No compelling benefit before WPF spike |

No framework has a documented guarantee of zero focus or fullscreen interference. Measure WPF behavior before committing to Phase 1. If native WPF transparency is unreliable, retain .NET Core/domain and change only shell technology.
