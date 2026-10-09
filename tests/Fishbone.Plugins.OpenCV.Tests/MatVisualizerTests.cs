using OpenCvSharp;

namespace Fishbone.Plugins.OpenCV.Tests;

/// <summary>
/// The OpenCV plugin lets the debugger show a <see cref="Mat"/>. It turns BGR into RGB, and scales
/// depths other than 8-bit with the same rule as HALCON images.
/// </summary>
public class MatVisualizerTests
{
    [Fact]
    public void Plugin_RegistersAVisualizerForMat()
    {
        var config = new FishboneConfiguration();
        new OpenCVPlugin().Register(config);
        using var mat = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));

        Assert.True(config.CanVisualize(mat));
        Assert.NotNull(config.Visualize(mat));
    }

    [Fact]
    public void ToImage_Gray8_KeepsThePixels()
    {
        using var mat = Mat.FromPixelData(2, 2, MatType.CV_8UC1, new byte[] { 1, 2, 3, 4 });

        var image = OpenCVVisualizer.ToImage(mat)!;

        Assert.Equal((2, 2, 1), (image.Width, image.Height, image.Channels));
        Assert.Equal([1, 2, 3, 4], image.Pixels);
    }

    [Fact]
    public void ToImage_Bgr8_BecomesRgb()
    {
        using var mat = Mat.FromPixelData(1, 1, MatType.CV_8UC3, new byte[] { 1, 2, 3 });

        var image = OpenCVVisualizer.ToImage(mat)!;

        Assert.Equal(3, image.Channels);
        Assert.Equal([3, 2, 1], image.Pixels);
    }

    [Fact]
    public void ToImage_Bgra8_BecomesRgba()
    {
        using var mat = Mat.FromPixelData(1, 1, MatType.CV_8UC4, new byte[] { 1, 2, 3, 4 });

        var image = OpenCVVisualizer.ToImage(mat)!;

        Assert.Equal(4, image.Channels);
        Assert.Equal([3, 2, 1, 4], image.Pixels);
    }

    [Fact]
    public void ToImage_Float_IsScaledToEightBit()
    {
        using var mat = Mat.FromPixelData(1, 3, MatType.CV_32FC1, new float[] { 0.5f, 1.5f, 1f });

        var image = OpenCVVisualizer.ToImage(mat)!;

        Assert.Equal([0, 255, 128], image.Pixels);
    }

    [Fact]
    public void ToImage_SixteenBit_IsScaledToEightBit()
    {
        using var mat = Mat.FromPixelData(1, 2, MatType.CV_16UC1, new ushort[] { 1000, 3000 });

        var image = OpenCVVisualizer.ToImage(mat)!;

        Assert.Equal([0, 255], image.Pixels);
    }

    [Fact]
    public void ToImage_Region_UsesOnlyItsPixels()
    {
        // a region of interest shares its parent's memory, so its rows aren't next to each other
        using var parent = Mat.FromPixelData(3, 3, MatType.CV_8UC1, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        using var region = new Mat(parent, new Rect(1, 1, 2, 2));

        var image = OpenCVVisualizer.ToImage(region)!;

        Assert.Equal((2, 2), (image.Width, image.Height));
        Assert.Equal([5, 6, 8, 9], image.Pixels);
    }

    [Theory]
    [MemberData(nameof(UnshowableMats))]
    public void CanShow_RejectsMatsThatArentImages(Mat mat)
    {
        Assert.False(OpenCVVisualizer.CanShow(mat));
    }

    public static TheoryData<Mat> UnshowableMats() => new()
    {
        new Mat(),
        new Mat(2, 2, MatType.CV_8UC2, Scalar.All(0)),
    };
}
