using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary>
/// HALCON operators in a debugger's watch: they return nothing, so the watch shows their out values.
/// </summary>
public class HalconWatchTests
{
    private static (FishboneEnvironment Values, FishboneConfiguration Config) RunRegionsSample()
    {
        string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "halcon_regions.fb"));
        var config = new FishboneConfiguration()
            .AddBuiltIn("println", new Action<object?>(_ => { }))
            .AddPlugin(new HalconOperatorPlugin());
        return (FishboneProgram.Run(script, config), config);
    }

    [HalconFact]
    public void CountObj_ShowsTheCount()
    {
        var (values, config) = RunRegionsSample();

        object? count = FishboneExpression.Evaluate("count_obj(blobs, out n)", values, config);

        Assert.Equal(values.GetValue("blob_count"), count);
    }

    [HalconFact]
    public void SelectObj_GivesARegionThePreviewCanShow()
    {
        var (values, config) = RunRegionsSample();

        object? first = FishboneExpression.Evaluate("select_obj(blobs, out first, 1)", values, config);

        var region = Assert.IsType<HObject>(first);
        Assert.True(config.CanVisualize(region));
    }

    [HalconFact]
    public void SeveralOuts_ShowByName()
    {
        var (values, config) = RunRegionsSample();

        var outs = Assert.IsAssignableFrom<IDictionary<string, object?>>(
            FishboneExpression.Evaluate("area_center(blobs, out area, out row, out column)", values, config));

        Assert.Equal(["area", "row", "column"], outs.Keys);
    }
}
