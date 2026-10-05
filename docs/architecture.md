# Architecture

[Documentation index](README.md)

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

## Process tracking and focus

1. Subscribe to process-start events when a launch begins, then launch through Steam or the configured executable/URI.
2. Identify matching processes by install directory or explicit executable override; exclude common launchers, installers, and crash/anti-cheat helpers for Steam.
3. Track exit using Windows process handles. Allow a short handoff grace period and reconcile on exit.
4. Stop watching when the tracked session finishes or startup detection times out.

WMI process-start events can be denied for standard Windows accounts. The app does **not** request elevation: it falls back to bounded startup checks for up to 90 seconds and then uses process-exit events. Protected processes, launchers that live outside a game's install directory, and unusual multi-process games can require an explicit custom entry/process override. An unknown launch is reported rather than recording invented playtime.

Only sessions launched through this app are actively tracked. Steam's subsequent metadata changes still update local history for other launches. Imported Steam minutes and locally estimated totals are reconciled with `max`, not added together; this avoids straightforward double-counting but is not a full Steam cloud history synchronizer.

The overlay closes during launch. After a confirmed exit it reopens if the option is enabled **and the desktop has focus**. If another app has focus, it offers a tray notification instead of stealing focus. A launch failure or timeout is reported through the tray; the overlay returns automatically when the desktop has focus.
