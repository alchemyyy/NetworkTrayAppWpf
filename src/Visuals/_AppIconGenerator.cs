using System.IO;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWPF.Visuals;

/// <summary>
/// Developer-only tool: generates the application icon (app.ico)
/// by rendering a placeholder glyph across multiple resolutions
/// and packing them into a single multi-image .ico file.
///
/// Invoked automatically at the start of <see cref="Program.Main"/> in Debug builds,
/// so the icon stays in sync with the renderer.
/// The resulting app.ico is picked up at compile time
/// via &lt;ApplicationIcon&gt; in NetworkTrayAppWPF.csproj.
/// Swap the glyph constant below to rebrand the skeleton.
/// </summary>
public static class AppIconGenerator
{
    // Render each size natively (no downscaling) so glyphs stay crisp at every shell-surface size Windows may request.
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    // Network tray app icon: Ethernet glyph baked into app.ico so the file-explorer / shortcut /
    // taskbar-overflow representation reads as a network app even before the runtime tray icon paints.
    private const string DefaultIconGlyph = GlyphCatalog.ETHERNET;

    /// <summary>
    /// Renders the placeholder glyph at each size in <see cref="IconSizes"/>
    /// and writes a PNG-encoded multi-image .ico to <paramref name="outputPath"/>.
    /// The caller picks <paramref name="foreground"/>; production calls pass white so the icon
    /// reads correctly on Windows' default dark taskbar / Alt-Tab surfaces.
    /// </summary>
    public static void Generate(string outputPath, Color foreground)
    {
        List<byte[]> pngs = new(IconSizes.Length);
        foreach (int size in IconSizes)
        {
            RenderTargetBitmap bitmap = TrayIconRenderer.RenderBitmap(size, DefaultIconGlyph, foreground);
            pngs.Add(EncodePng(bitmap));
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using FileStream fs = new(outputPath, FileMode.Create, FileAccess.Write);
        WriteIco(fs, IconSizes, pngs);
    }

    private static byte[] EncodePng(RenderTargetBitmap bitmap)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream ms = new();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void WriteIco(Stream stream, int[] sizes, List<byte[]> pngs)
    {
        using BinaryWriter writer = new(stream);
        int count = sizes.Length;

        // ICONDIR header
        writer.Write((ushort)0);        // reserved
        writer.Write((ushort)1);        // type: icon
        writer.Write((ushort)count);    // image count

        // Each ICONDIRENTRY is 16 bytes; image data follows immediately after the directory.
        int imageOffset = 6 + 16 * count;

        for (int i = 0; i < count; i++)
        {
            int size = sizes[i];
            // .ico encodes 256 as 0 in the single-byte width/height fields.
            byte dim = size >= 256 ? (byte)0 : (byte)size;

            writer.Write(dim);                       // bWidth
            writer.Write(dim);                       // bHeight
            writer.Write((byte)0);                   // bColorCount (0 for >=256 colors)
            writer.Write((byte)0);                   // bReserved
            writer.Write((ushort)1);                 // wPlanes
            writer.Write((ushort)32);                // wBitCount
            writer.Write((uint)pngs[i].Length);      // dwBytesInRes
            writer.Write((uint)imageOffset);         // dwImageOffset

            imageOffset += pngs[i].Length;
        }

        foreach (byte[] png in pngs)
            writer.Write(png);
    }
}
