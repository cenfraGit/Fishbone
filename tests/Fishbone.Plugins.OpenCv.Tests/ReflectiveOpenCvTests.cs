using Fishbone.Engine;
using Fishbone.Plugins.OpenCv;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv.Tests;

public class ReflectiveOpenCvTests
{
    private static FishboneConfiguration ConfigWithOpenCv(Mat source)
    {
        var config = new FishboneConfiguration();
        config.AddPlugin(new OpenCvPlugin());
        config.AddValue("src", source);
        return config;
    }

    [Fact]
    public void CvtColor_WritesResultBackIntoScriptAllocatedMat()
    {
        using var src = new Mat(rows: 4, cols: 6, type: MatType.CV_8UC3, s: new Scalar(128, 64, 32));
        var config = ConfigWithOpenCv(src);

        var env = FishboneEngine.Run("""
let dst = Mat();
cv.CvtColor(src, dst, "BGR2GRAY");
""", config);

        var dst = Assert.IsType<Mat>(env.GetValue("dst"));
        Assert.False(dst.Empty());        // the op actually ran and filled dst
        Assert.Equal(1, dst.Channels());  // BGR -> single gray channel
        Assert.Equal(4, dst.Rows);
        Assert.Equal(6, dst.Cols);
    }

    [Fact]
    public void Resize_UsesListToSizeConverterAndOptionalDefaults()
    {
        // exercises the [w, h] list -> Size converter and the omitted optional fx/fy/interpolation
        using var src = new Mat(rows: 4, cols: 6, type: MatType.CV_8UC1, s: Scalar.All(255));
        var config = ConfigWithOpenCv(src);

        var env = FishboneEngine.Run("""
let dst = Mat();
cv.Resize(src, dst, [3, 2]);
""", config);

        var dst = Assert.IsType<Mat>(env.GetValue("dst"));
        Assert.Equal(2, dst.Rows);   // size is (width, height) -> 3 cols, 2 rows
        Assert.Equal(3, dst.Cols);
    }

    [Fact]
    public void ReturnValueOperation_FlowsBackToScript()
    {
        // cv.CountNonZero returns an int directly (no output Mat), proving return-style ops bind too
        using var src = new Mat(rows: 2, cols: 2, type: MatType.CV_8UC1, s: Scalar.All(255));
        var config = ConfigWithOpenCv(src);

        var env = FishboneEngine.Run("let n = cv.CountNonZero(src);", config);

        Assert.Equal(4, Convert.ToInt32(env.GetValue("n")));
    }

    [Fact]
    public void StaticConstants_AreReadable()
    {
        using var src = new Mat(rows: 1, cols: 1, type: MatType.CV_8UC1, s: Scalar.All(0));

        var env = FishboneEngine.Run("""
let filled = cv.FILLED;
let pi = cv.PI;
""", ConfigWithOpenCv(src));

        Assert.Equal(-1, Convert.ToInt32(env.GetValue("filled")));
        Assert.Equal(Math.PI, Convert.ToDouble(env.GetValue("pi")), 10);
    }

    [Fact]
    public void MatStaticFactory_IsReachable()
    {
        using var src = new Mat(rows: 1, cols: 1, type: MatType.CV_8UC1, s: Scalar.All(0));

        var env = FishboneEngine.Run("""
let zeros = Mat.Zeros(4, 6, MatType.CV_8UC1).ToMat();
""", ConfigWithOpenCv(src));

        var zeros = Assert.IsType<Mat>(env.GetValue("zeros"));
        Assert.Equal(4, zeros.Rows);
        Assert.Equal(6, zeros.Cols);
    }

    [Fact]
    public void GenericStaticFactory_ReportsAsMissingMember()
    {
        using var src = new Mat(rows: 1, cols: 1, type: MatType.CV_8UC1, s: Scalar.All(0));

        var exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run(
            "let m = Mat.FromArray([1, 2, 3]);", ConfigWithOpenCv(src)));

        Assert.Contains("does not have a public member named 'FromArray'", exception.Message);
    }

    [Fact]
    public void EdgeDetectSampleCallNames_AllResolve()
    {
        using var src = new Mat(rows: 8, cols: 8, type: MatType.CV_8UC1, s: Scalar.All(128));

        var env = FishboneEngine.Run("""
let blurred = Mat();
cv.GaussianBlur(src, blurred, [5, 5], 0);
let edges = Mat();
cv.Canny(blurred, edges, 50, 150);
let width = src.Width;
""", ConfigWithOpenCv(src));

        Assert.False(Assert.IsType<Mat>(env.GetValue("edges")).Empty());
        Assert.Equal(8, Convert.ToInt32(env.GetValue("width")));
    }
}