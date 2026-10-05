# Scripts

Run these commands from the repository root. Scripts locate the checkout relative
to their own location.

| Script | Purpose |
| --- | --- |
| `build.ps1` | Build Release; `-Test` runs assertions; `-Publish` writes `artifacts/GameLibraryOverlay_v2026.10.1` |
| `run.ps1` | Open the app; publish first if the executable is missing |
| `verify-ui.ps1` | Run isolated local-library, stress, and idle diagnostics |
| `measure-resources.ps1` | Sample a running app's CPU and memory |

```powershell
.\scripts\build.ps1 -Test -Publish
.\scripts\run.ps1
# Exit the app from its tray menu before this check.
.\scripts\verify-ui.ps1
# Start the app again before sampling.
.\scripts\measure-resources.ps1
```

Resource measurement accepts `-TargetProcessId`, `-OutputDirectory`, and
`-Checkpoints` (seconds). Defaults are 30, 60, 90, 300, and 600 seconds.
See [development](../docs/development.md) and
[diagnostics](../docs/performance-and-diagnostics.md).

Publishing uses `artifacts/GameLibraryOverlay_vYYYY.MM.X`, read from
`Directory.Build.props`, and includes project and artwork licenses automatically.
`Get-PublishDirectory.ps1` is the shared path helper used by the scripts.

Publishing also creates `artifacts/GameLibraryOverlay_vYYYY.MM.X.zip` with the
files directly at the archive root, including the licenses. Repeating `-Publish`
replaces the same version's ZIP.

The release targets Windows x64 only. Single-file publishing bundles application
DLLs into `GameLibrary.exe` and omits debugging symbols. The ZIP contains exactly
`GameLibrary.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md` at its root.
The x64 .NET 10 Windows Desktop Runtime is required.
