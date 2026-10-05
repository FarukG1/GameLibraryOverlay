# Troubleshooting

[Documentation index](README.md)

| Problem | What to check |
| --- | --- |
| Compatible SDK missing | Install an SDK compatible with `global.json`; check `dotnet --info` |
| App cannot start | Install the .NET 10 Windows Desktop Runtime; keep the published directory together |
| Source changes do not appear | Run `.\scripts\build.ps1 -Test -Publish` |
| Steam shortcut missing | Exit Steam normally, reopen it, and choose Rescan Steam |
| Library disconnected | Reconnect and rescan; cached entries remain unavailable until rediscovered |
| Custom session not detected | Set Game process executable if the launch target hands off |
| Controller actions duplicate | Check Steam Input desktop keyboard mappings |
| Process remains after hiding | Choose Exit in the tray menu to quit |
| UI verification fails immediately | Exit the normal app; single-instance forwarding takes precedence |
| Wallpaper Engine pauses | Use the focused-app rule in the performance guide |
| Global shortcut fails | Check Settings for conflicts and select another shortcut |

Only sessions started through Game Library are actively tracked. Protected or
unusual multi-process games may require an executable override. WMI denial uses
bounded startup checks rather than requesting elevation.

See [usage](usage.md), [tracking](architecture.md), and
[diagnostics](performance-and-diagnostics.md).
