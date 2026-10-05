# Performance and diagnostics

[Documentation index](README.md)

## Wallpaper and performance

The overlay is a normal borderless window sized to the monitor's **work area**, leaving the taskbar usable. An auto-hidden taskbar gets an edge gap so it can be revealed. The window is not topmost or exclusive fullscreen. Transparency reveals whatever is underneath. Show the desktop first to see Wallpaper Engine behind it. There is no desktop reparenting, wallpaper injection, or automatic minimization of other apps.

Wallpaper Engine can classify large borderless windows as fullscreen/maximized. In **Wallpaper Engine → Settings → Performance → Application rules**, create/edit a rule for **GameLibrary.exe**, choose **Is focused**, and **Keep running**. A focus-based rule applies while browsing without forcing wallpapers to run during games just because this tray process exists. A fullscreen-only exception may not apply when the taskbar is visible. This app leaves Wallpaper Engine's configuration untouched. [Wallpaper Engine application-rule documentation](https://help.wallpaperengine.io/en/functionality/applicationrules.html)

- Tray startup does not create the overlay.
- Closing the overlay destroys its visual tree and cancels pending cover work.
- Horizontal carousels and vertical rows use virtualization and recycling.
- Images are decoded for the card's physical size, monitor DPI, and crop, retaining native source detail instead of always reducing images to 360 pixels. Low-resolution Steam originals cannot gain detail through enlargement. There are two concurrent decodes and a 64 MiB image-cache budget. The cache trims to 16 MiB when the overlay closes.
- Filesystem events are debounced. There is no recurring idle scan, animation timer, or running-game polling loop.
- A user-scoped mutex and current-user-only named pipe enforce a single instance.
- Monitor placement uses physical work-area bounds with per-monitor DPI awareness; missing monitors fall back to the primary display.

Historical development measurements (not a performance guarantee): A synthetic 5,000-game overlay realized only 12 game-card controls. Five-second background-only samples measured 0–16 ms additional CPU, approximately 107 MiB working set and 59 MiB private memory. These are point measurements, not guarantees. UI screenshot diagnostics temporarily allocate additional memory and are not representative of tray-only use. GPU utilization and Wallpaper Engine behavior still need testing during real gameplay.

Game statistics are last-played time and playtime from local Steam files, plus sessions observed by this app. No usage telemetry is uploaded. Diagnostic CPU time is processor time consumed during a measurement interval; working set is resident RAM (including shared runtime pages), and private bytes are memory committed solely to this process. They are different measures, not values to add together.

## UI diagnostics

Use an isolated data directory so verification cannot alter normal preferences. These modes exit automatically; close an existing app instance before running them because single-instance forwarding intentionally wins over diagnostic arguments.

```powershell
.\artifacts\GameLibraryOverlay_v2026.10.1\GameLibrary.exe --smoke-test --data-dir "C:\path\to\project\artifacts\smoke"
.\artifacts\GameLibraryOverlay_v2026.10.1\GameLibrary.exe --smoke-test --stress --data-dir "C:\path\to\project\artifacts\stress"
.\artifacts\GameLibraryOverlay_v2026.10.1\GameLibrary.exe --background --idle-diagnostics --data-dir "C:\path\to\project\artifacts\idle"
```

Smoke mode reads the real Steam library, exercises second-instance forwarding, renders overlay/settings PNGs, and checks closing the overlay. Stress mode substitutes 5,000 in-memory entries. Neither launches a real game. The test suite uses only its own `TestGame.exe` for launch/exit verification.

Manual release checks: tray actions and start-with-Windows; shortcut conflict feedback; custom launcher handoffs; mixed-DPI monitor changes; game focus restoration; Wallpaper Engine composition; and GPU use with the overlay closed.

### Background performance capture

Run `.\scripts\measure-resources.ps1` while Game Library is running. It samples the app process every second for ten minutes and writes `samples.csv`, a cumulative `report.md`, and checkpoint reports at 30, 60, 90, 300, and 600 seconds under `artifacts/performance-<timestamp>`. Each checkpoint also summarizes the interval since the previous one. CPU is normalized across the PC's logical processors; the CSV also provides one-core utilization. RAM means resident working set, while private commit is allocated memory. GPU and game frame times are not measured. The sampler stops if the original app process exits, rather than attaching to another process.
