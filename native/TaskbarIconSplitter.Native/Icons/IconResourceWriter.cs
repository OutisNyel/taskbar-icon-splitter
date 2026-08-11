using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TaskbarIconSplitter.Native.Icons;

internal static class IconResourceWriter
{
    private const int IconSize = 64;

    internal static void WritePngIcon(
        Bitmap source,
        string path)
    {
        using var resized = new Bitmap(
            IconSize,
            IconSize,
            PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, IconSize, IconSize);
        }

        using var png = new MemoryStream();
        resized.Save(png, ImageFormat.Png);
        var pngBytes = png.ToArray();

        using var output = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new BinaryWriter(output);

        // ICONDIR
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);

        // ICONDIRENTRY followed by a PNG-compressed image.
        writer.Write((byte)IconSize);
        writer.Write((byte)IconSize);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)pngBytes.Length);
        writer.Write((uint)22);
        writer.Write(pngBytes);
    }
}
