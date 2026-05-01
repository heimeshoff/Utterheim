# RasteriseLogo

One-shot helper that turns `assets/branding/mockingbird-logo.svg` into the PNG +
`.ico` artefacts the WPF app and styleguide reference.

## Why it exists

The build host doesn't have ImageMagick or Inkscape on PATH, but it does have the
.NET SDK. SkiaSharp + Svg.Skia rasterise SVG cleanly, and the `.ico` container
format is small enough to assemble by hand.

## When to re-run

Only when the source SVG (`assets/branding/mockingbird-logo.svg`) changes. The
generated PNGs and `.ico` are committed alongside the SVG, so day-to-day builds
do not need this tool.

## How to run

```
dotnet run --project Tools\RasteriseLogo\RasteriseLogo.csproj
```

The tool writes its outputs back into `assets\branding\`:

- `mockingbird-logo-{16,24,32,48,64,128,256,512}.png`
- `mockingbird.ico` (multi-resolution: 16/24/32/48/64/128/256, all PNG-compressed)

## Why it isn't in `mockingbird.sln`

Keeping it standalone means `dotnet build mockingbird.sln` doesn't pull
SkiaSharp / Svg.Skia into the main build, which has no use for them at runtime.
The helper is regenerated on demand and its outputs (committed PNG/ICO files)
are what the WPF app consumes.
