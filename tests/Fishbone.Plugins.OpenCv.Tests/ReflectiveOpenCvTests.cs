using Fishbone.Engine;
using Fishbone.Plugins.OpenCv;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv.Tests;

public class ReflectiveOpenCvTests
{
    private static FishboneConfiguration ConfigWithOpenCv(Mat source)
    {
        var config = new FishboneConfiguration();
        new OpenCvPlugin().Register(config);
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
cv_cvt_color(src, dst, "BGR2GRAY");
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
cv_resize(src, dst, [3, 2]);
""", config);

        var dst = Assert.IsType<Mat>(env.GetValue("dst"));
        Assert.Equal(2, dst.Rows);   // size is (width, height) -> 3 cols, 2 rows
        Assert.Equal(3, dst.Cols);
    }

    [Fact]
    public void ReturnValueOperation_FlowsBackToScript()
    {
        // cv_count_non_zero returns an int directly (no output Mat), proving return-style ops bind too
        using var src = new Mat(rows: 2, cols: 2, type: MatType.CV_8UC1, s: Scalar.All(255));
        var config = ConfigWithOpenCv(src);

        var env = FishboneEngine.Run("let n = cv_count_non_zero(src);", config);

        Assert.Equal(4, Convert.ToInt32(env.GetValue("n")));
    }
}