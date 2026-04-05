using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RunFence.Core;

/// <summary>
/// Builds multi-size ICO files from caller-supplied per-frame draw logic.
/// </summary>
public static class IconBuilder
{
    /// <summary>
    /// Creates a multi-size <see cref="Icon"/> by rendering each size using the provided draw action.
    /// </summary>
    /// <param name="sizes">Icon sizes to include (e.g. 16, 32, 48, 256).</param>
    /// <param name="drawFrame">Called once per size to render the icon frame into the provided Graphics context.</param>
    public static Icon CreateMultiSizeIcon(int[] sizes, Action<Graphics, int> drawFrame)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICO header
        writer.Write((short)0); // Reserved
        writer.Write((short)1); // ICO type
        writer.Write((short)sizes.Length);

        // Placeholder for directory entries (16 bytes each)
        var directoryStart = ms.Position;
        for (int i = 0; i < sizes.Length; i++)
            writer.Write(new byte[16]);

        var imageOffsets = new long[sizes.Length];
        var imageSizes = new int[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            imageOffsets[i] = ms.Position;

            using var bmp = new Bitmap(sizes[i], sizes[i], PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                drawFrame(g, sizes[i]);
            }

            using var pngStream = new MemoryStream();
            bmp.Save(pngStream, ImageFormat.Png);
            var pngBytes = pngStream.ToArray();
            writer.Write(pngBytes);
            imageSizes[i] = pngBytes.Length;
        }

        // Write directory entries
        ms.Position = directoryStart;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // Width
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // Height
            writer.Write((byte)0); // Color palette
            writer.Write((byte)0); // Reserved
            writer.Write((short)1); // Color planes
            writer.Write((short)32); // Bits per pixel
            writer.Write(imageSizes[i]);
            writer.Write((int)imageOffsets[i]);
        }

        ms.Position = 0;
        return new Icon(ms);
    }
}