using System.Runtime.InteropServices;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary>
/// <see cref="HalconImageConversion"/> turns the planes HALCON hands out into one interleaved image.
/// Non-byte pixel types are scaled with the same rule as non-8-bit OpenCV images.
/// </summary>
public class HalconImageConversionTests
{
    [Fact]
    public void FromPlanes_OneBytePlane_IsGray()
    {
        using var plane = Pin(new byte[] { 1, 2, 3, 4 });

        var image = HalconImageConversion.FromPlanes("byte", 2, 2, [plane.Address])!;

        Assert.Equal((2, 2, 1), (image.Width, image.Height, image.Channels));
        Assert.Equal([1, 2, 3, 4], image.Pixels);
    }

    [Fact]
    public void FromPlanes_ThreeBytePlanes_AreInterleavedAsRgb()
    {
        using var red = Pin(new byte[] { 10, 11 });
        using var green = Pin(new byte[] { 20, 21 });
        using var blue = Pin(new byte[] { 30, 31 });

        var image = HalconImageConversion.FromPlanes("byte", 2, 1, [red.Address, green.Address, blue.Address])!;

        Assert.Equal(3, image.Channels);
        Assert.Equal([10, 20, 30, 11, 21, 31], image.Pixels);
    }

    [Fact]
    public void FromPlanes_Uint2_IsScaledToEightBit()
    {
        using var plane = Pin(new ushort[] { 1000, 3000, 2000 });

        var image = HalconImageConversion.FromPlanes("uint2", 3, 1, [plane.Address])!;

        Assert.Equal([0, 255, 128], image.Pixels);
    }

    [Fact]
    public void FromPlanes_Int2_KeepsTheSign()
    {
        using var plane = Pin(new short[] { -100, 100 });

        var image = HalconImageConversion.FromPlanes("int2", 2, 1, [plane.Address])!;

        Assert.Equal([0, 255], image.Pixels);
    }

    [Fact]
    public void FromPlanes_Real_IsScaledToEightBit()
    {
        using var plane = Pin(new float[] { 0.25f, 0.75f });

        var image = HalconImageConversion.FromPlanes("real", 2, 1, [plane.Address])!;

        Assert.Equal([0, 255], image.Pixels);
    }

    [Fact]
    public void FromPlanes_ThreeRealPlanes_ShareOneRange()
    {
        // like OpenCV, channels share the range, so colors keep their balance
        using var red = Pin(new float[] { 0f });
        using var green = Pin(new float[] { 50f });
        using var blue = Pin(new float[] { 100f });

        var image = HalconImageConversion.FromPlanes("real", 1, 1, [red.Address, green.Address, blue.Address])!;

        Assert.Equal([0, 128, 255], image.Pixels);
    }

    [Theory]
    [InlineData("complex")]
    [InlineData("vector_field_relative")]
    public void FromPlanes_UnsupportedPixelType_IsNull(string pixelType)
    {
        using var plane = Pin(new byte[16]);

        Assert.Null(HalconImageConversion.FromPlanes(pixelType, 1, 1, [plane.Address]));
    }

    private static PinnedBuffer Pin(Array data) => new(data);

    private sealed class PinnedBuffer(Array data) : IDisposable
    {
        private GCHandle _handle = GCHandle.Alloc(data, GCHandleType.Pinned);

        public IntPtr Address => _handle.AddrOfPinnedObject();

        public void Dispose() => _handle.Free();
    }
}
