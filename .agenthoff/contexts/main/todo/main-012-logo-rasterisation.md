---
id: main-012
title: Rasterise the speaking-person logo to PNG sizes + .ico for tray
status: todo
type: chore
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-010]
blocks: []
tags: [branding, assets, follow-up]
---

## Why

`main-010` produced the styleguide and a placeholder SVG at
`assets/branding/mockingbird-logo.svg`. The WPF main window can render the SVG-derived
geometry inline, but two surfaces need real raster artwork:

- **System tray** — Windows tray icons require an `.ico` file (multi-resolution). The
  current build still uses WPF UI's default tray icon.
- **About page, taskbar, future installer artwork** — want PNG sizes 16/24/32/48/64/
  128/256/512.

This task takes whatever final SVG the user supplies (or the placeholder, if the user is
happy with it as a starting point) and produces those raster outputs.

## What

1. Take the user-supplied final SVG (or the existing placeholder at
   `assets/branding/mockingbird-logo.svg` if the user has signed off on it as-is).
2. Generate PNGs at sizes 16, 24, 32, 48, 64, 128, 256, 512 — write them to
   `assets/branding/` as `mockingbird-logo-<size>.png`.
3. Generate a multi-resolution `mockingbird.ico` (containing the 16/24/32/48/64/128/256
   layers at minimum) — write to `assets/branding/mockingbird.ico`.
4. Wire the `.ico` into the WPF app:
   - Add the `.ico` to `Mockingbird.csproj` as a `<Resource>` (or via
     `<ApplicationIcon>` for the assembly icon).
   - Bind the `tray:NotifyIcon` `Icon` property to the `.ico` resource so the tray
     visibly shows the speaking-person silhouette.
   - Set `<ApplicationIcon>` in the csproj so the .exe shows the icon in Explorer /
     taskbar.
5. Wire a larger PNG (256 px) into the About page when that page lands.

## Acceptance criteria

- [ ] `assets/branding/mockingbird.ico` exists and contains the documented size layers.
- [ ] `assets/branding/mockingbird-logo-{16,24,32,48,64,128,256,512}.png` all exist.
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
