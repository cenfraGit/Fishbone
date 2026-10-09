using System.Buffers.Binary;
using System.IO.Compression;

namespace Fishbone.Debugging;

/// <summary>
/// An image a debugger can show: 8-bit pixels, row by row with no padding. One channel is gray,
/// three are RGB and four are RGBA. Plugins turn their own image types into this, and the
/// debugger encodes it the same way for all of them.
/// <para>
/// It can also carry regions and contours to draw over the pixels, or be only shapes, with no
/// pixels at all. A debugger draws shapes alone on an empty canvas, or over the image it's
/// already showing.
/// </para>
/// </summary>
public sealed class FishboneImage
{
    public FishboneImage(int width, int height, int channels, byte[] pixels)
    {
        ThrowIfWrongShape(width, height, channels, pixels.Length);
        Width = width;
        Height = height;
        Channels = channels;
        Pixels = pixels;
    }

    // only shapes, sized to hold them
    private FishboneImage(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = [];
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>1, 3 or 4, or 0 for an image that's only shapes.</summary>
    public int Channels { get; }
    public byte[] Pixels { get; }

    /// <summary>False for an image that's only shapes.</summary>
    public bool HasPixels => Channels > 0;

    /// <summary>Regions to draw over the image, in its pixel coordinates.</summary>
    public IReadOnlyList<FishboneRegion> Regions { get; init; } = [];

    /// <summary>Contours to draw over the image, in its pixel coordinates.</summary>
    public IReadOnlyList<FishboneContour> Contours { get; init; } = [];

    /// <summary>
    /// An image that's only shapes. Its size reaches from row and column 0 to the shapes' farthest
    /// pixel, so they keep their place when drawn over a real image.
    /// </summary>
    public static FishboneImage FromShapes(IReadOnlyList<FishboneRegion> regions, IReadOnlyList<FishboneContour> contours)
    {
        int width = 1, height = 1;
        foreach (var region in regions)
        {
            if (region.Rows.Length > 0)
                height = Math.Max(height, region.Rows.Max() + 1);
            if (region.ColumnEnds.Length > 0)
                width = Math.Max(width, region.ColumnEnds.Max() + 1);
        }
        // a point belongs to the pixel whose center is nearest
        foreach (var contour in contours)
        {
            if (contour.Rows.Length > 0)
                height = Math.Max(height, (int)Math.Floor(contour.Rows.Max() + 0.5) + 1);
            if (contour.Columns.Length > 0)
                width = Math.Max(width, (int)Math.Floor(contour.Columns.Max() + 0.5) + 1);
        }
        return new FishboneImage(width, height) { Regions = regions, Contours = contours };
    }

    /// <summary>
    /// Builds an image from values of any range, like a float or 16-bit image. The lowest value
    /// becomes 0 and the highest 255, like HDevelop shows them. A constant image is black.
    /// </summary>
    public static FishboneImage FromValues(int width, int height, int channels, double[] values)
    {
        ThrowIfWrongShape(width, height, channels, values.Length);

        // one range for every channel, so a color image keeps its balance
        double min = values.Min();
        double range = values.Max() - min;
        var pixels = new byte[values.Length];
        if (range > 0)
            for (int i = 0; i < values.Length; i++)
                pixels[i] = (byte)Math.Round((values[i] - min) / range * 255);

        return new FishboneImage(width, height, channels, pixels);
    }

    /// <summary>
    /// Encodes the image as PNG. An image whose longest side is over <paramref name="maxSide"/>
    /// is scaled down first, keeping its proportions.
    /// </summary>
    public byte[] ToPng(int maxSide = 2048)
    {
        if (!HasPixels)
            throw new InvalidOperationException("The image is only shapes, it has no pixels to encode.");
        double scale = Math.Min(1, (double)maxSide / Math.Max(Width, Height));
        int width = Math.Max(1, (int)Math.Round(Width * scale));
        int height = Math.Max(1, (int)Math.Round(Height * scale));

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = Channels switch { 1 => 0, 3 => 2, _ => 6 };
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + width * Channels];
            for (int y = 0; y < height; y++)
            {
                // nearest pixel. row[0] is the filter type, 0 for none
                int sourceY = y * Height / height;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = x * Width / width;
                    Pixels.AsSpan((sourceY * Width + sourceX) * Channels, Channels).CopyTo(row.AsSpan(1 + x * Channels));
                }
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);

        var typeAndData = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
            typeAndData[i] = (byte)type[i];
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        output.Write(number);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static void ThrowIfWrongShape(int width, int height, int channels, int length)
    {
        if (width < 1 || height < 1)
            throw new ArgumentException($"An image needs a positive size, not {width}x{height}.");
        if (channels is not (1 or 3 or 4))
            throw new ArgumentException($"An image has 1, 3 or 4 channels, not {channels}.");
        if (length != width * height * channels)
            throw new ArgumentException($"A {width}x{height} image with {channels} channels needs {width * height * channels} values, not {length}.");
    }
}
