# Using Game Library

[Documentation index](README.md)

## Run

Open `artifacts\GameLibraryOverlay_v2026.10.1\GameLibrary.exe` after building, or run:

```powershell
.\scripts\run.ps1
```

The first normal launch opens the library. **Ctrl+Alt+G** toggles it. **Escape** closes the overlay; the tray app continues running. Right-click the tray icon for Open Library, Settings, Rescan Steam, Start with Windows, and Exit. Startup registration is off unless explicitly enabled.

- Click a cover to launch. Right-click it to change favorites or category memberships.
- Ctrl+F reveals the search field; Escape dismisses search and clears its filter before hiding the library on the next press. Use arrow keys to navigate and Enter to launch a selected card. The bottom-right hint displays the configured global show/hide shortcut.
- The first game receives focus automatically. Xbox/XInput: D-pad or left stick navigates, A launches, B closes, X toggles favorites, Y opens Settings; bumpers change rows. Controller input is read only while the library is visible and active, with no controller polling while hidden or behind Settings. Steam Input desktop keyboard mappings can duplicate native input; use an ordinary XInput gamepad mapping for this app if that occurs.
- The mouse wheel scrolls rows; Shift+wheel scrolls a carousel. Thin grey scrollbars appear only while the pointer is directly over the scrollbar track. Controller navigation hides them until the mouse moves or clicks again, even if the pointer was left over a track. There are no row arrow buttons.
- Recently Played prefers Steam's wide cover artwork (custom wide grid, wide_cover, library_header, or header), without adding a separate logo over it. It fills the card with a centered, aspect-preserving crop: wide images lose the excess at the left and right, without distortion or letterboxing. A background and logo are used only if no wide cover is available. Other rows use portrait artwork.
- Right-click empty library space for All games, Favorites, Settings, and Hide library. All games and Favorites are also available in the tray Library view submenu; Rescan Steam remains in the tray menu.
- Left/right navigation moves the carousel under a fixed left-hand selection slot, including the last game. The selected cover is 6% larger; its title and playtime remain normal size.
- The bottom-right footer shows Xbox A/B/X/Y icons before keyboard keycaps, including the configured toggle shortcut. Kenney CC0 artwork is bundled locally; see [third-party notices](../THIRD_PARTY_NOTICES.md).
- Both wide and portrait covers have rounded, clipped corners. Appearance provides a smooth, editable 0–40 px corner-radius control (default 12 px). Selection border thickness is adjustable from 0–10 px (default 2; 0 hides the outline). Selection and hover outlines surround only the artwork, with room for the enlarged cover at the left edge. Appearance defaults to Windows accent color (updated automatically); uncheck it to choose a custom color using the native picker or a #RRGGBB value.
- Settings lets you choose the monitor, transparency, cover size, corner radius, global shortcut, Steam installation/account, additional library roots, categories, and custom games.
- Add custom games with **Settings → Custom games → Add**. Enter an executable or launcher URI. Optional arguments apply to executables. An optional launcher executable is started first; it is not waited on. Use a launcher URI for games that require their launcher's own startup workflow.
- Set **Game process executable** if the launch target hands off to a different executable. URI-only entries can launch without an override, but session detection is then unavailable.
- Category names entered on a custom game must be defined in the Categories tab. Save settings commits changes and keeps the editor open. Close discards any changes made since the last save. Appearance sliders move smoothly and have editable percentage/pixel values.
- Non-Steam games added through Steam are discovered automatically from the selected account's `shortcuts.vdf`; they launch through Steam using its stored shortcut ID. Their Steam custom artwork is imported too. Steam can keep recent edits in memory until it writes that file: if a new shortcut is missing, exit Steam normally, reopen it, and rescan. Adding a second copy in this app is unnecessary.
