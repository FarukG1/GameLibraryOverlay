# Release checklist

[Documentation index](README.md)

## First public launch

- Review the existing `LICENSE` and include it in the first commit.
- Keep third-party notices and bundled asset licenses intact.
- Review staged files and history for credentials, personal data, and diagnostics.
- Keep `.tools/`, `artifacts/`, `bin/`, and `obj/` out of Git.
- Add the remote and push when ready.
- Set the GitHub description and topics; check rendered documentation links.
- Confirm the Windows build workflow passes on GitHub.
- Consider a screenshot with synthetic games and no personal data.
- If accepting private vulnerability reports, enable GitHub private reporting and
  document the actual reporting channel.

## Validate a release

```powershell
.\scripts\build.ps1 -Test -Publish
.\scripts\verify-ui.ps1
```

Exit the normal app before publishing and UI verification. Check manually:

- Tray actions, startup registration, shortcut conflicts.
- Keyboard, mouse, and XInput navigation.
- Custom launcher handoffs and session completion.
- Mixed-DPI monitors and focus restoration.
- Wallpaper Engine composition and GPU use with the overlay closed.

Use `.\scripts\measure-resources.ps1` when measurements are needed. CPU and RAM
samples do not establish game FPS or GPU overhead.

## Package and describe

The first release is **2026.10.1**, with Git tag `v2026.10.1`.
The version is set centrally in `Directory.Build.props` and embedded in builds.
Use `YYYY.MM.X`: year and month identify the release date, while X is a global
release counter that increments for every release and never resets. For example:
`2026.10.1`, `2026.10.2`, `2026.11.3`, `2027.01.4`.

Publishing creates the release ZIP automatically beside the published folder.

The build includes the project license and third-party notice automatically. The folder name
tracks the version in `Directory.Build.props`; update the example version for
future releases.

```powershell
.\scripts\build.ps1 -Test -Publish
Get-FileHash artifacts/GameLibraryOverlay_v2026.10.1.zip -Algorithm SHA256
```

In GitHub Releases, choose tag `v2026.10.1`, title `Game Library 2026.10.1`,
and upload **GameLibraryOverlay_v2026.10.1.zip**. Publish the SHA-256 hash in the
release notes, along with the Windows and .NET 10 Windows Desktop Runtime
requirements. GitHub automatically supplies source archives from the tag.
The ZIP is a portable framework-dependent application, not an installer.
Users extract the entire archive and run `GameLibrary.exe`.

Do not upload test fixtures, logs, screenshots from real libraries, package
caches, `bin/`, `obj/`, or the entire `artifacts/` folder.

Keep the complete `artifacts/GameLibraryOverlay_v2026.10.1` directory together. Include the project license,
third-party notices (including the artwork license) alongside distributed packages. The current
build requires the .NET 10 Windows Desktop Runtime.

Write release notes describing actual changes, requirements, limitations, and
validation. Test on a clean Windows environment before publishing.

The release targets Windows x64 only. Single-file publishing bundles application
DLLs into `GameLibrary.exe` and omits debugging symbols. The ZIP contains exactly
`GameLibrary.exe`, `LICENSE`, and `THIRD_PARTY_NOTICES.md` at its root.
The x64 .NET 10 Windows Desktop Runtime is required.
