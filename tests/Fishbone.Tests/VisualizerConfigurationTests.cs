using Fishbone.Debugging;

namespace Fishbone.Tests;

/// <summary>
/// <see cref="FishboneConfiguration.AddVisualizer"/> registers how the debugger shows a .NET type
/// as an image. Plugins use it for their image types.
/// </summary>
public class VisualizerConfigurationTests
{
    private static FishboneImage Gray(byte value) => new(1, 1, 1, [value]);

    [Fact]
    public void Visualize_UsesTheVisualizerForTheType()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => Gray(picture.Level));

        Assert.True(config.CanVisualize(new Picture(9)));
        Assert.Equal([9], config.Visualize(new Picture(9))!.Pixels);
    }

    [Fact]
    public void Visualize_CoversDerivedTypes()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => Gray(picture.Level));

        Assert.True(config.CanVisualize(new Photo(3)));
        Assert.Equal([3], config.Visualize(new Photo(3))!.Pixels);
    }

    [Fact]
    public void Visualize_ExactTypeWinsOverABaseType()
    {
        var config = new FishboneConfiguration()
            .AddVisualizer<Picture>(_ => Gray(1))
            .AddVisualizer<Photo>(_ => Gray(2));

        Assert.Equal([2], config.Visualize(new Photo(0))!.Pixels);
        Assert.Equal([1], config.Visualize(new Picture(0))!.Pixels);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData("text")]
    public void Visualize_OtherValues_HaveNoImage(object? value)
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => Gray(picture.Level));

        Assert.False(config.CanVisualize(value));
        Assert.Null(config.Visualize(value));
    }

    [Fact]
    public void CanShow_FiltersValuesOfTheType()
    {
        // like a HALCON object that holds a region instead of an image
        var config = new FishboneConfiguration()
            .AddVisualizer<Picture>(picture => Gray(picture.Level), canShow: picture => picture.Level > 0);

        Assert.True(config.CanVisualize(new Picture(1)));
        Assert.False(config.CanVisualize(new Picture(0)));
        Assert.Null(config.Visualize(new Picture(0)));
    }

    [Fact]
    public void CanVisualize_DoesNotBuildTheImage()
    {
        int built = 0;
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture =>
        {
            built++;
            return Gray(picture.Level);
        });

        config.CanVisualize(new Picture(1));

        Assert.Equal(0, built);
    }

    [Fact]
    public void Clone_KeepsVisualizers()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => Gray(picture.Level));

        Assert.True(config.Clone().CanVisualize(new Picture(1)));
    }

    [Fact]
    public void ImageChildren_ComeFromTheVisualizer()
    {
        var config = new FishboneConfiguration()
            .AddVisualizer<Picture>(picture => Gray(picture.Level), children: picture => [("[1]", new Picture(1)), ("[2]", new Picture(2))]);

        var children = config.Clone().ImageChildren(new Picture(9))!;

        Assert.Equal(["[1]", "[2]"], children.Select(child => child.Name));
    }

    [Fact]
    public void ImageChildren_AreNullWithoutAWayToSplit()
    {
        var config = new FishboneConfiguration().AddVisualizer<Picture>(picture => Gray(picture.Level));

        Assert.Null(config.ImageChildren(new Picture(9)));
        Assert.Null(config.ImageChildren("not an image"));
    }

    public class Picture(byte level)
    {
        public byte Level { get; } = level;
    }

    public sealed class Photo(byte level) : Picture(level);
}
