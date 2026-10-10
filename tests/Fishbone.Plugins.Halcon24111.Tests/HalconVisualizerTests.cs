using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary>
/// <see cref="HalconVisualizer"/> shows images as pixels, and regions and XLD contours as shapes
/// in image coordinates.
/// </summary>
public class HalconVisualizerTests
{
    [HalconFact]
    public void Image_ShowsItsPixels()
    {
        HOperatorSet.GenImageConst(out HObject image, "byte", 4, 3);

        var shown = HalconVisualizer.ToImage(image)!;

        Assert.True(HalconVisualizer.CanShow(image));
        Assert.True(shown.HasPixels);
        Assert.Equal((4, 3), (shown.Width, shown.Height));
        Assert.Empty(shown.Regions);
    }

    [HalconFact]
    public void SeveralImages_AreOneChildEach()
    {
        HOperatorSet.GenImageConst(out HObject first, "byte", 4, 3);
        HOperatorSet.GenImageConst(out HObject second, "byte", 5, 3);
        HOperatorSet.GenImageConst(out HObject third, "byte", 6, 3);
        HOperatorSet.ConcatObj(first, second, out HObject two);
        HOperatorSet.ConcatObj(two, third, out HObject three);

        var children = HalconVisualizer.Images(three)!;

        Assert.Equal(["[1]", "[2]", "[3]"], children.Select(child => child.Name));
        Assert.All(children, child => Assert.True(HalconVisualizer.CanShow((HObject)child.Value!)));
        Assert.Equal(5, HalconVisualizer.ToImage((HObject)children[1].Value!)!.Width);
        Assert.Equal(6, HalconVisualizer.ToImage((HObject)children[2].Value!)!.Width);
    }

    [HalconFact]
    public void OneImage_HasNoChildren()
    {
        HOperatorSet.GenImageConst(out HObject image, "byte", 4, 3);

        Assert.Null(HalconVisualizer.Images(image));
    }

    [HalconFact]
    public void SeveralRegions_HaveNoChildren()
    {
        HOperatorSet.GenCircle(out HObject first, 10, 10, 3);
        HOperatorSet.GenCircle(out HObject second, 30, 40, 5);
        HOperatorSet.ConcatObj(first, second, out HObject both);

        Assert.Null(HalconVisualizer.Images(both));
    }

    // a checked image in an array is found again after a step through its path, like images[2]
    [HalconFact]
    public void ImageInAnArray_IsReachedByIndexFromOne()
    {
        HOperatorSet.GenImageConst(out HObject first, "byte", 4, 3);
        HOperatorSet.GenImageConst(out HObject second, "byte", 5, 3);
        HOperatorSet.ConcatObj(first, second, out HObject both);
        var config = new FishboneConfiguration();
        new HalconOperatorPlugin().Register(config);
        var environment = new FishboneEnvironment();
        environment.Declare("images", both);

        var picked = FishboneExpression.Evaluate("images[2]", environment, config);

        Assert.Equal(5, HalconVisualizer.ToImage((HObject)picked!)!.Width);
    }

    [HalconFact]
    public void Rectangle_IsOneRunPerRow()
    {
        // rows 2 to 4, columns 5 to 7, both ends included
        HOperatorSet.GenRectangle1(out HObject rectangle, 2, 5, 4, 7);

        var shown = HalconVisualizer.ToImage(rectangle)!;

        Assert.True(HalconVisualizer.CanShow(rectangle));
        Assert.False(shown.HasPixels);
        var region = Assert.Single(shown.Regions);
        Assert.Equal([2, 3, 4], region.Rows);
        Assert.Equal([5, 5, 5], region.ColumnStarts);
        Assert.Equal([7, 7, 7], region.ColumnEnds);
        Assert.Equal((8, 5), (shown.Width, shown.Height));
    }

    [HalconFact]
    public void SeveralRegions_AreAllShown()
    {
        HOperatorSet.GenCircle(out HObject first, 10, 10, 3);
        HOperatorSet.GenCircle(out HObject second, 30, 40, 5);
        HOperatorSet.ConcatObj(first, second, out HObject both);

        var shown = HalconVisualizer.ToImage(both)!;

        Assert.Equal(2, shown.Regions.Count);
        Assert.Contains(30, shown.Regions[1].Rows);
    }

    [HalconFact]
    public void Contour_KeepsItsSubpixelPoints()
    {
        HOperatorSet.GenContourPolygonXld(out HObject contour, new HTuple(1.5, 2.5, 8.25), new HTuple(3.0, 6.5, 4.75));

        var shown = HalconVisualizer.ToImage(contour)!;

        Assert.True(HalconVisualizer.CanShow(contour));
        var line = Assert.Single(shown.Contours);
        Assert.Equal([1.5, 2.5, 8.25], line.Rows);
        Assert.Equal([3.0, 6.5, 4.75], line.Columns);
    }

    [HalconFact]
    public void Polygon_ShowsAsAContour()
    {
        HOperatorSet.GenContourPolygonXld(out HObject contour, new HTuple(0.0, 0.0, 10.0, 10.0), new HTuple(0.0, 10.0, 10.0, 0.0));
        HOperatorSet.GenPolygonsXld(contour, out HObject polygon, "ramer", 1);

        var shown = HalconVisualizer.ToImage(polygon)!;

        Assert.True(HalconVisualizer.CanShow(polygon));
        Assert.Equal([0.0, 0.0, 10.0, 10.0], Assert.Single(shown.Contours).Rows);
    }

    [HalconFact]
    public void EmptyRegion_ShowsNothingWithoutFailing()
    {
        HOperatorSet.GenEmptyRegion(out HObject empty);

        var shown = HalconVisualizer.ToImage(empty)!;

        Assert.Empty(Assert.Single(shown.Regions).Rows);
    }

    [HalconFact]
    public void EmptyObject_IsNotShown()
    {
        HOperatorSet.GenEmptyObj(out HObject nothing);

        Assert.False(HalconVisualizer.CanShow(nothing));
    }

    [HalconFact]
    public void RegionsSample_MakesShapesTheDebuggerCanShow()
    {
        string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "halcon_regions.fb"));
        var config = new FishboneConfiguration()
            .AddBuiltIn("println", new Action<object?>(_ => { }))
            .AddPlugin(new HalconOperatorPlugin());

        var env = FishboneProgram.Run(script, config);

        foreach (string name in new[] { "blobs", "edges", "outlines" })
        {
            var value = (HObject)env.GetValue(name);
            Assert.True(HalconVisualizer.CanShow(value), name);
            var shown = HalconVisualizer.ToImage(value)!;
            Assert.False(shown.HasPixels, name);
            Assert.True(shown.Regions.Count + shown.Contours.Count > 0, name);
        }
    }
}
