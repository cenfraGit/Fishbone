using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Fishbone.Debugging;

namespace Fishbone.Debugging.Tests;

/// <summary>
/// <see cref="FishboneImage"/> is the one shape every plugin turns its images into, and it encodes
/// to PNG the same way for all of them.
/// </summary>
public class FishboneImageTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 2)]
    [InlineData(4, 6)]
    public void ToPng_WritesSignatureAndHeader(int channels, byte colorType)
    {
        var image = new FishboneImage(3, 2, channels, new byte[3 * 2 * channels]);

        var png = Parse(image.ToPng());

        Assert.Equal(3, png.Width);
        Assert.Equal(2, png.Height);
        Assert.Equal(8, png.BitDepth);
        Assert.Equal(colorType, png.ColorType);
    }

    [Fact]
    public void ToPng_PixelsRoundTrip()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        var image = new FishboneImage(3, 2, 3, pixels);

        var png = Parse(image.ToPng());

        // each row starts with its filter type, 0 for none
        byte[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 10, 11, 12, 13, 14, 15, 16, 17, 18];
        Assert.Equal(expected, png.Data);
    }

    [Fact]
    public void ToPng_EndsWithIend()
    {
        var png = Parse(new FishboneImage(1, 1, 1, [0]).ToPng());

        Assert.Equal("IEND", png.ChunkTypes[^1]);
    }

    [Fact]
    public void ToPng_LargeImage_IsScaledDownKeepingItsProportions()
    {
        var image = new FishboneImage(4000, 1000, 1, new byte[4000 * 1000]);

        var png = Parse(image.ToPng());

        Assert.Equal(2048, png.Width);
        Assert.Equal(512, png.Height);
        Assert.Equal((1 + 2048) * 512, png.Data.Length);
    }

    [Fact]
    public void ToPng_SmallImage_KeepsItsSize()
    {
        var png = Parse(new FishboneImage(640, 480, 1, new byte[640 * 480]).ToPng());

        Assert.Equal(640, png.Width);
        Assert.Equal(480, png.Height);
    }

    [Fact]
    public void ToPng_ScaledDown_SamplesThePixels()
    {
        // a 4x1 image with a white right half, halved, keeps a black and a white pixel
        var image = new FishboneImage(4, 1, 1, [0, 0, 255, 255]);

        var png = Parse(image.ToPng(maxSide: 2));

        Assert.Equal([0, 0, 255], png.Data);
    }

    [Fact]
    public void FromValues_ScalesLowestToZeroAndHighestTo255()
    {
        var image = FishboneImage.FromValues(4, 1, 1, [10, 30, 20, 10]);

        Assert.Equal([0, 255, 128, 0], image.Pixels);
    }

    [Fact]
    public void FromValues_UsesOneRangeForAllChannels()
    {
        // channels share the range, so colors keep their balance
        var image = FishboneImage.FromValues(1, 1, 3, [0, 50, 100]);

        Assert.Equal([0, 128, 255], image.Pixels);
    }

    [Fact]
    public void FromValues_ConstantImage_IsBlack()
    {
        var image = FishboneImage.FromValues(2, 1, 1, [7.5, 7.5]);

        Assert.Equal([0, 0], image.Pixels);
    }

    [Theory]
    [InlineData(2, 2, 1, 3)]
    [InlineData(2, 2, 2, 8)]
    [InlineData(0, 2, 1, 0)]
    public void Constructor_RejectsWrongShape(int width, int height, int channels, int length)
    {
        Assert.ThrowsAny<ArgumentException>(() => new FishboneImage(width, height, channels, new byte[length]));
    }

    private sealed record Png(int Width, int Height, byte BitDepth, byte ColorType, byte[] Data, List<string> ChunkTypes);

    // reads the chunks, checks every crc, and inflates the pixel data
    private static Png Parse(byte[] bytes)
    {
        Assert.Equal(PngSignature, bytes[..8]);
        int offset = 8;
        var types = new List<string>();
        var compressed = new MemoryStream();
        int width = 0, height = 0;
        byte bitDepth = 0, colorType = 0;

        while (offset < bytes.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var data = bytes.AsSpan(offset + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length));
            Assert.Equal(Crc32(bytes.AsSpan(offset + 4, 4 + length)), crc);

            types.Add(type);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
            }
            else if (type == "IDAT")
            {
                compressed.Write(data);
            }
            offset += 12 + length;
        }

        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
        var raw = new MemoryStream();
        inflate.CopyTo(raw);
        return new Png(width, height, bitDepth, colorType, raw.ToArray(), types);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
