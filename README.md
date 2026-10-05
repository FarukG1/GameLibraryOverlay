# Game Library

A local Windows game-library overlay for Steam and custom games, built with C#,
.NET 10, and WPF. Browse cover carousels, organize favorites and categories, and
launch games with a keyboard, mouse, or Xbox/XInput controller.

## Features

- Discover installed Steam games and non-Steam shortcuts from local Steam files.
- Add custom executables and launcher URIs.
- Track sessions launched through the app and import local Steam playtime.
- Customize covers, accent color, transparency, and monitor placement.
- Open the library from the tray or a configurable global shortcut.
- Keep settings and diagnostics locally, with no usage telemetry.

The overlay leaves the taskbar accessible. It shows installed and cached titles
rather than your entire uninstalled Steam account library.

## Quick start

Use Windows and install a .NET 10 SDK compatible with [global.json](global.json).
PowerShell 7 is recommended; Visual Studio is optional.

From the repository root:

```powershell
.\scripts\build.ps1 -Test -Publish
.\scripts\run.ps1
```

The first normal launch opens the library. **Ctrl+Alt+G** toggles it; **Escape**
hides it while the tray app keeps running. Click a cover or press **Enter** to
launch. Right-click the tray icon to open Settings or exit.

You can also open `artifacts\GameLibraryOverlay_v2026.10.1\GameLibrary.exe` after publishing. Keep the entire
published directory together. The target computer needs the .NET 10 Windows
Desktop Runtime for this framework-dependent build.

## Documentation

| Guide | Contents |
| --- | --- |
| [Usage](docs/usage.md) | Controls, appearance, categories, custom games, Steam shortcuts |
| [Development](docs/development.md) | Setup, commands, scripts, tests |
| [Architecture](docs/architecture.md) | Components, session tracking, focus |
| [Data and privacy](docs/data-and-privacy.md) | Local files, discovery, recovery |
| [Performance and diagnostics](docs/performance-and-diagnostics.md) | Wallpaper Engine, UI checks, measurements |
| [Troubleshooting](docs/troubleshooting.md) | Setup, discovery, launch problems |
| [Release checklist](docs/releasing.md) | Public launch and release preparation |

## Repository layout

```text
.github/        Issue and pull request templates; Windows build workflow
docs/           User and developer guides
scripts/        Build, run, verification, and resource measurement
src/            Core, infrastructure, and WPF application projects
tests/          Regression runner and test process
GameLibrary.slnx
```

Generated output goes into `artifacts/`; CLI state and package caches go into
`.tools/`. Both are ignored by Git.

## Contributing

Bug reports, documentation improvements, and code contributions are welcome.
Read [CONTRIBUTING.md](CONTRIBUTING.md) for setup and review expectations.

## License and credits

See [LICENSE](LICENSE) for the GNU Affero General Public License, version 3.
Bundled input prompts are Kenney CC0 artwork; see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for attribution.

The release targets Windows x64 only. Single-file publishing bundles application
DLLs into `GameLibrary.exe` and omits debugging symbols. The ZIP contains exactly
`GameLibrary.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md` at its root.
The x64 .NET 10 Windows Desktop Runtime is required.
