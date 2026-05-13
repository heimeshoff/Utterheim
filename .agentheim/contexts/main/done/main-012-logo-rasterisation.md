---
id: main-012
title: Rasterise the speaking-person logo to PNG sizes + .ico for tray
status: done
type: chore
context: main
created: 2026-05-01
completed: 2026-05-01
commit:
depends_on: [main-010]
blocks: []
tags: [branding, assets, follow-up]
---

## Why

`main-010` produced the styleguide and a placeholder SVG at
`assets/branding/utterheim-logo.svg`. The WPF main window can render the SVG-derived
geometry inline, but two surfaces need real raster artwork:

- **System tray** — Windows tray icons require an `.ico` file (multi-resolution). The
  current build still uses WPF UI's default tray icon.
- **About page, taskbar, future installer artwork** — want PNG sizes 16/24/32/48/64/
  128/256/512.

This task takes whatever final SVG the user supplies (or the placeholder, if the user is
happy with it as a starting point) and produces those raster outputs.

## What

1. Take the user-supplied final SVG (or the existing placeholder at
   `assets/branding/utterheim-logo.svg` if the user has signed off on it as-is).
2. Generate PNGs at sizes 16, 24, 32, 48, 64, 128, 256, 512 — write them to
   `assets/branding/` as `utterheim-logo-<size>.png`.
3. Generate a multi-resolution `utterheim.ico` (containing the 16/24/32/48/64/128/256
   layers at minimum) — write to `assets/branding/utterheim.ico`.
4. Wire the `.ico` into the WPF app:
   - Add the `.ico` to `Utterheim.csproj` as a `<Resource>` (or via
     `<ApplicationIcon>` for the assembly icon).
   - Bind the `tray:NotifyIcon` `Icon` property to the `.ico` resource so the tray
     visibly shows the speaking-person silhouette.
   - Set `<ApplicationIcon>` in the csproj so the .exe shows the icon in Explorer /
     taskbar.
5. Wire a larger PNG (256 px) into the About page when that page lands.

## Acceptance criteria

- [ ] `assets/branding/utterheim.ico` exists and contains the documented size layers.
- [ ] `assets/branding/utterheim-logo-{16,24,32,48,64,128,256,512}.png` all exist.
- [ ] Running the app shows the speaking-person silhouette in the system tray (replaces
      the WPF UI default).
- [ ] Running the app shows the speaking-person silhouette as the .exe icon in
      Explorer / taskbar.
- [ ] The styleguide's "Brand assets" table is updated to mark the placeholder rows as
      finalised, and references the produced files.

## Notes

- Tooling: any of Inkscape (CLI: `inkscape --export-type=png --export-filename=...`),
  ImageMagick (`magick convert`), or a small node script using `sharp` works. Pick
  whatever matches the host environment at the time.
- This task assumes `main-010`'s sign-off has happened — otherwise the SVG that gets
  rasterised is still officially a placeholder.

## Outcome

Rasterised the approved placeholder SVG into PNG sizes 16/24/32/48/64/128/256/512 plus
a multi-resolution `utterheim.ico` (16/24/32/48/64/128/256 layers, PNG-compressed).
The build host had no ImageMagick or Inkscape available, so a one-shot helper at
`Tools\RasteriseLogo\` (SkiaSharp + Svg.Skia) was written; it lives outside
`utterheim.sln` so it doesn't pollute the main build graph. The helper substitutes
`currentColor` with white before rasterising so the silhouette reads cleanly on the
dark Windows 11 taskbar / Mica backdrop, and the `.ico` is assembled by hand using the
straightforward ICONDIR / ICONDIRENTRY layout with PNG-compressed frames.

WPF wiring:

- `<ApplicationIcon>..\..\assets\branding\utterheim.ico</ApplicationIcon>` in
  `Utterheim.csproj` — the .exe now shows the silhouette in Explorer / taskbar.
- `<Resource Include="..\..\assets\branding\utterheim.ico" Link="Resources\utterheim.ico" />`
  to make it resolvable via the pack URI.
- `tray:NotifyIcon Icon="pack://application:,,,/Resources/utterheim.ico"` in
  `Views\MainWindow.xaml` — replaces the WPF UI default tray icon with the silhouette.
- Same pack URI on `ui:FluentWindow Icon="..."` so the title-bar / Alt-Tab also pick
  up the silhouette.

Build verification: `dotnet build utterheim.sln -c Debug` reports 0 warnings,
0 errors.

The visual rendering of the tray icon and `.exe` icon is for the user to verify
(Worker has no live tray to inspect). The PNG content was confirmed clean (white
silhouette on transparent, no stray colour pixels) by decoding via PIL during the
authoring loop.

Key files:
- `Tools\RasteriseLogo\RasteriseLogo.csproj`, `Program.cs`, `README.md`
- `assets\branding\utterheim-logo-{16,24,32,48,64,128,256,512}.png`
- `assets\branding\utterheim.ico`
- `src\Utterheim\Utterheim.csproj`
- `src\Utterheim\Views\MainWindow.xaml`
- `docs\styleguide.md`
- `.agentheim\contexts\main\README.md`
