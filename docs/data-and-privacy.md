# Data and privacy

[Documentation index](README.md)

Settings and logs stay on your computer; usage telemetry is not uploaded.
Steam and external launchers may have their own network activity. Before sharing
logs, settings, or screenshots, redact personal paths and Steam account IDs.

## Local data

Normal runs store data in `%LOCALAPPDATA%\GameLibrary`:

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
