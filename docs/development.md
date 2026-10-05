# Development

[Documentation index](README.md)

## Requirements

- Windows: WPF, Windows Forms tray integration, and Windows APIs are required.
- .NET 10 SDK compatible with `global.json` (10.0.401 with `latestFeature` roll-forward).
- PowerShell; PowerShell 7 is recommended.
- Access to nuget.org for initial restore.

Visual Studio is optional. The SDK includes the Windows Desktop Runtime.
Scripts check `C:\Program Files\dotnet\dotnet.exe`, then fall back to `dotnet`
on PATH. The checkout can be on any drive.

## Build and test

Run from the repository root:

Exit a running published instance from its tray menu before using `-Publish`;
Windows locks loaded application files while it is running.

```powershell
.\scripts\build.ps1
.\scripts\build.ps1 -Test
.\scripts\build.ps1 -Test -Publish
```

Builds use Release configuration. `Directory.Build.props` enables nullable
references, warnings as errors, and deterministic builds. `NuGet.Config` selects
nuget.org. The only explicit production NuGet dependency is `System.Management`,
used for optional Windows process-start events.

Tests are an executable assertion runner, rather than a `dotnet test` project.
`-Test` treats a nonzero exit code as failure. Fixtures live under
`artifacts\test-data`; process tests launch only the helper `TestGame.exe`.

## Run and inspect

```powershell
.\scripts\run.ps1
.\scripts\verify-ui.ps1
```

The run script publishes if the executable is missing. Rebuild and publish after
source changes because an existing executable is reused.

Exit the normal instance before UI verification. Verification briefly opens
windows and uses isolated preferences. See [diagnostics](performance-and-diagnostics.md).

## Generated files

The publish directory is `artifacts/GameLibraryOverlay_vYYYY.MM.X`, using the
version in `Directory.Build.props`. Build and publish output automatically include
`LICENSE`, `THIRD_PARTY_NOTICES.md`, and `INPUT_ASSETS_LICENSE.txt`.

| Location | Contents |
| --- | --- |
| `.tools/dotnet/` | Local .NET CLI state |
| `.tools/packages/` | NuGet cache |
| `**/bin/`, `**/obj/` | Build and intermediate output |
| `artifacts/GameLibraryOverlay_v2026.10.1/` | Published application |
| `artifacts/test-data/` | Test fixtures |
| `artifacts/verify-*/` | UI verification data |

Scripts resolve paths relative to the checkout and can be invoked by absolute
path from another working directory. Normal preferences live in
`%LOCALAPPDATA%\GameLibrary`. Game files and Steam metadata are not modified.

## Continuous integration

The Windows GitHub Actions workflow runs `-Test -Publish` on pushes and pull
requests and supports manual runs. Interactive UI verification remains local
because it needs a Windows desktop.

Publishing also creates `artifacts/GameLibraryOverlay_vYYYY.MM.X.zip` with the
files directly at the archive root, including the licenses. Repeating `-Publish`
replaces the same version's ZIP.
