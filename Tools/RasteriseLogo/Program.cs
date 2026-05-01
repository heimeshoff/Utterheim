// One-shot rasteriser for the Mockingbird placeholder logo.
//
// Reads ../../assets/branding/mockingbird-logo.svg, produces PNGs at the
// canonical sizes plus a multi-resolution mockingbird.ico (PNG-compressed
// frames, which Windows accepts since Vista).

using SkiaSharp;
using Svg.Skia;

namespace Mockingbird.Tools.RasteriseLogo;

internal static class Program
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256, 512 };
    private static readonly int[] IcoSizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main()
    {
        var repoRoot = FindRepoRoot();
        var brandingDir = Path.Combine(repoRoot, "assets", "branding");
        var svgPath = Path.Combine(brandingDir, "mockingbird-logo.svg");

        if (!File.Exists(svgPath))
        {
            Console.Error.WriteLine($"Source SVG not found: {svgPath}");
            return 1;
        }

        // The placeholder SVG uses fill="currentColor" / stroke="currentColor".
        // Svg.Skia treats unresolved currentColor as black. For a Windows tray
        // icon on a dark taskbar / Mica backdrop we want a light silhouette, so
        // substitute white before rasterising.
        var svgText = File.ReadAllText(svgPath)
            .Replace("currentColor", "#FFFFFF");

        using var svg = new SKSvg();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgText));
        svg.Load(ms);

        if (svg.Picture is null)
        {
            Console.Error.WriteLine("Failed to load SVG picture.");
            return 2;
        }

        var pngBytesBySize = new Dictionary<int, byte[]>();
        foreach (var size in Sizes)
        {
            var png = Rasterise(svg, size);
            var pngPath = Path.Combine(brandingDir, $"mockingbird-logo-{size}.png");
            File.WriteAllBytes(pngPath, png);
            pngBytesBySize[size] = png;
            Console.WriteLine($"Wrote {pngPath} ({png.Length} bytes)");
        }

        var icoPath = Path.Combine(brandingDir, "mockingbird.ico");
        WriteIco(icoPath, IcoSizes.Select(s => (s, pngBytesBySize[s])).ToList());
        Console.WriteLine($"Wrote {icoPath}");

        return 0;
    }

    private static byte[] Rasterise(SKSvg svg, int size)
    {
        // Use an SKSurface (the recommended draw target) backed by an Unpremul
        // ImageInfo so the snapshot and PNG encode see straight RGBA. Skia
        // handles the alpha math internally between draw-time premul and
        // surface-pixels unpremul.
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            throw new InvalidOperationException($"Failed to create SKSurface ({size}x{size}).");
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var picture = svg.Picture!;
        var bounds = picture.CullRect;
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        canvas.Scale(scale, scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void WriteIco(string path, IReadOnlyList<(int Size, byte[] Png)> frames)
    {
        // ICO format: 6-byte header + 16-byte ICONDIRENTRY per frame + payload.
        // Each frame's payload is the PNG itself (modern Windows accepts this).
        const int headerSize = 6;
        const int dirEntrySize = 16;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR
        bw.Write((ushort)0);            // reserved
        bw.Write((ushort)1);            // type = icon
        bw.Write((ushort)frames.Count); // image count

        var offset = headerSize + dirEntrySize * frames.Count;
        foreach (var (size, png) in frames)
        {
            // 256 is encoded as 0 in the 1-byte width/height fields.
            var widthByte = (byte)(size >= 256 ? 0 : size);
            var heightByte = (byte)(size >= 256 ? 0 : size);

            bw.Write(widthByte);
            bw.Write(heightByte);
            bw.Write((byte)0);          // colour palette (none for true colour)
            bw.Write((byte)0);          // reserved
            bw.Write((ushort)1);        // colour planes
            bw.Write((ushort)32);       // bits per pixel
            bw.Write((uint)png.Length); // size in bytes
            bw.Write((uint)offset);     // offset from start of file

            offset += png.Length;
        }

        foreach (var (_, png) in frames)
        {
            bw.Write(png);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "mockingbird.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (mockingbird.sln).");
    }
}
