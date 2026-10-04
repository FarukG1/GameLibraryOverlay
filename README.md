# Game Library

A local Windows game-library overlay built with C# / .NET 10 / WPF. Steam discovery, a tray app, virtualized cover carousels, favorites, custom categories, manual launcher entries, and launch/session tracking. No Electron, browser runtime, Steam Web API, Millennium plugin, or telemetry.

## Run

Open `artifacts\app\GameLibrary.exe` after building, or run:

```powershell
.\run.ps1
```

The first normal launch opens the library. **Ctrl+Alt+G** toggles it. **Escape** closes the overlay; the tray app continues running. Right-click the tray icon for Open Library, Settings, Rescan Steam, Start with Windows, and Exit. Startup registration is off unless explicitly enabled.

- Click a cover to launch. Right-click it to change favorites or category memberships.
- Ctrl+F reveals the search field; Escape dismisses search and clears its filter before hiding the library on the next press. Use arrow keys to navigate and Enter to launch a selected card. The bottom-right hint displays the configured global show/hide shortcut.
- The first game receives focus automatically. Xbox/XInput: D-pad or left stick navigates, A launches, B closes, X toggles favorites, Y opens Settings; bumpers change rows. Controller input is read only while the library is visible and active, with no controller polling while hidden or behind Settings. Steam Input desktop keyboard mappings can duplicate native input; use an ordinary XInput gamepad mapping for this app if that occurs.
- The mouse wheel scrolls rows; Shift+wheel scrolls a carousel. Thin grey scrollbars appear only while the pointer is directly over the scrollbar track. Controller navigation hides them until the mouse moves or clicks again, even if the pointer was left over a track. There are no row arrow buttons.
- Recently Played prefers Steam's wide cover artwork (custom wide grid, wide_cover, library_header, or header), without adding a separate logo over it. It fills the card with a centered, aspect-preserving crop: wide images lose the excess at the left and right, without distortion or letterboxing. A background and logo are used only if no wide cover is available. Other rows use portrait artwork.
- Right-click empty library space for All games, Favorites, Settings, and Hide library. All games and Favorites are also available in the tray Library view submenu; Rescan Steam remains in the tray menu.
- Left/right navigation moves the carousel under a fixed left-hand selection slot, including the last game. The selected cover is 6% larger; its title and playtime remain normal size.
- The bottom-right footer shows Xbox A/B/X/Y icons before keyboard keycaps, including the configured toggle shortcut. Kenney CC0 artwork is bundled locally; see THIRD_PARTY_NOTICES.md.
- Both wide and portrait covers have rounded, clipped corners. Appearance provides a smooth, editable 0–40 px corner-radius control (default 12 px). Selection and hover outlines surround only the artwork.
- Settings lets you choose the monitor, transparency, cover size, corner radius, global shortcut, Steam installation/account, additional library roots, categories, and custom games.
- Add custom games with **Settings → Custom games → Add**. Enter an executable or launcher URI. Optional arguments apply to executables. An optional launcher executable is started first; it is not waited on. Use a launcher URI for games that require their launcher's own startup workflow.
- Set **Game process executable** if the launch target hands off to a different executable. URI-only entries can launch without an override, but session detection is then unavailable.
- Category names entered on a custom game must be defined in the Categories tab. Save settings commits changes and keeps the editor open. Close discards any changes made since the last save. Appearance sliders move smoothly and have editable percentage/pixel values.
- Non-Steam games added through Steam are discovered automatically from the selected account's `shortcuts.vdf`; they launch through Steam using its stored shortcut ID. Their Steam custom artwork is imported too. Steam can keep recent edits in memory until it writes that file: if a new shortcut is missing, exit Steam normally, reopen it, and rescan. Adding a second copy in this app is unnecessary.

## Build and verify

Install the .NET 10 SDK on C:. PowerShell 7 is recommended. No Visual Studio installation is required.

```powershell
.\build.ps1 -Test -Publish
```

The script builds Release with warnings treated as errors, runs a small executable test suite, and publishes a framework-dependent app into `artifacts\app`. Keep that entire directory together. The target PC needs the .NET 10 Windows Desktop Runtime (included with the installed SDK). `global.json` pins the SDK feature band used here with forward feature-band roll-forward.

After publishing, ` .\verify-ui.ps1 ` runs the local-library, 5,000-game, single-instance, and idle diagnostics. It briefly opens UI windows and closes them automatically. Exit any normally running instance before verification.

The project, NuGet package cache (`.tools\packages`), CLI files (`.tools\dotnet`), and build output stay in this workspace on **C:**. D: is read only as a game-library location. No game files are modified.

The only explicit production NuGet dependency is `System.Management`, for optional Windows process-start events. The tray uses the Windows Forms framework's `NotifyIcon`; all application windows are WPF. There is no service container or third-party UI framework.

## Layout

```text
src/
  GameLibrary.Core/             Game, preferences, session models, provider contract
  GameLibrary.Infrastructure/   SteamProvider, CustomGameProvider, Vdf
                               SettingsService, LibraryWatcher
                               GameLauncher, GameProcessMonitor
  GameLibrary.App/              OverlayUI (OverlayWindow), SettingsWindow
                               TrayService, hotkey, monitor placement, single instance
tests/
  GameLibrary.Tests/            Executable assertions; returns nonzero on failure
  TestGame/                    Harmless short-lived process used for session tests
```

`App` wires these small components together. There is deliberately no IntegrationServer or Millennium adapter. The provider contract leaves room for future local sources without an in-process plugin loader.

## Local data

Normal runs store data in `%LOCALAPPDATA%\GameLibrary` (C: on this machine):

| File | Purpose |
| --- | --- |
| `settings.json` | Settings, custom games, favorites, categories, observed sessions |
| `settings.json.bak` | Previous successfully saved settings |
| `library.json` | Rebuildable provider metadata cache |
| `covers\` | Content-addressed copies of manually chosen cover images |
| `app.log`, `app.log.1` | Bounded local diagnostics |

Writes use a temporary file, flush, and atomic replacement. A corrupt settings file is preserved before recovery from its backup. Unsupported settings versions stop startup instead of silently replacing user data. Library rescans never overwrite user preferences.

Steam discovery reads registry installation information, both `steamapps\libraryfolders.vdf` and `config\libraryfolders.vdf`, then `steamapps\appmanifest_*.acf` in each library. It reads playtime from the selected/recent account's `userdata\<account>\config\localconfig.vdf` and reads non-Steam shortcuts from that account's binary `config\shortcuts.vdf`. Shortcut IDs are imported as unsigned values; launch IDs use `(appid << 32) | 0x02000000`. Artwork supports Steam's old cache, per-app cache, hashed asset subdirectories, and custom portrait/wide/hero/logo artwork in `config\grid`. Steam metadata is never edited.

Known incomplete installs are listed as unavailable. Disconnected libraries preserve previously cached entries as unavailable; reconnect them and rescan. Steam's shared redistributable entry is excluded; other installed Steam applications, such as Wallpaper Engine, may appear alongside games. The app shows locally installed/cached titles, not your entire uninstalled Steam account library.

## Process tracking and focus

1. Subscribe to process-start events when a launch begins, then launch through Steam or the configured executable/URI.
2. Identify matching processes by install directory or explicit executable override; exclude common launchers, installers, and crash/anti-cheat helpers for Steam.
3. Track exit using Windows process handles. Allow a short handoff grace period and reconcile on exit.
4. Stop watching when the tracked session finishes or startup detection times out.

WMI process-start events can be denied for standard Windows accounts. The app does **not** request elevation: it falls back to bounded startup checks for up to 90 seconds and then uses process-exit events. On this PC, that fallback was exercised successfully. Protected processes, launchers that live outside a game's install directory, and unusual multi-process games can require an explicit custom entry/process override. An unknown launch is reported rather than recording invented playtime.

Only sessions launched through this app are actively tracked. Steam's subsequent metadata changes still update local history for other launches. Imported Steam minutes and locally estimated totals are reconciled with `max`, not added together; this avoids straightforward double-counting but is not a full Steam cloud history synchronizer.

The overlay closes during launch. After a confirmed exit it reopens if the option is enabled **and the desktop has focus**. If another app has focus, it offers a tray notification instead of stealing focus. A launch failure or timeout is reported through the tray; the overlay returns automatically when the desktop has focus.

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

Verification on the development PC found five installed Steam titles plus the GenshinImpact non-Steam shortcut. A synthetic 5,000-game overlay realized only 12 game-card controls. Five-second background-only samples measured 0–16 ms additional CPU, approximately 107 MiB working set and 59 MiB private memory. These are point measurements, not guarantees. UI screenshot diagnostics temporarily allocate additional memory and are not representative of tray-only use. GPU utilization and Wallpaper Engine behavior still need testing during real gameplay.

Game statistics are last-played time and playtime from local Steam files, plus sessions observed by this app. No usage telemetry is uploaded. Diagnostic CPU time is processor time consumed during a measurement interval; working set is resident RAM (including shared runtime pages), and private bytes are memory committed solely to this process. They are different measures, not values to add together.

## UI diagnostics

Use an isolated data directory so verification cannot alter normal preferences. These modes exit automatically; close an existing app instance before running them because single-instance forwarding intentionally wins over diagnostic arguments.

```powershell
.\artifacts\app\GameLibrary.exe --smoke-test --data-dir "C:\path\to\project\artifacts\smoke"
.\artifacts\app\GameLibrary.exe --smoke-test --stress --data-dir "C:\path\to\project\artifacts\stress"
.\artifacts\app\GameLibrary.exe --background --idle-diagnostics --data-dir "C:\path\to\project\artifacts\idle"
```

Smoke mode reads the real Steam library, exercises second-instance forwarding, renders overlay/settings PNGs, and checks closing the overlay. Stress mode substitutes 5,000 in-memory entries. Neither launches a real game. The test suite uses only its own `TestGame.exe` for launch/exit verification.

Manual release checks: tray actions and start-with-Windows; shortcut conflict feedback; custom launcher handoffs; mixed-DPI monitor changes; game focus restoration; Wallpaper Engine composition; and GPU use with the overlay closed.
