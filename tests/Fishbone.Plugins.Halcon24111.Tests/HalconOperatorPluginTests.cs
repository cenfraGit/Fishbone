using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary><see cref="HalconOperatorPlugin"/> registers every HALCON operator under its HDevelop name.</summary>
public class HalconOperatorPluginTests
{
    [Theory]
    [InlineData("ReadImage", "read_image")]
    [InlineData("GenImageGrayRamp", "gen_image_gray_ramp")]
    [InlineData("CountChannels", "count_channels")]
    public void ToSnakeCase_SplitsWordsAndLowersThem(string net, string script)
    {
        Assert.Equal(script, HalconOperatorPlugin.ToSnakeCase(net));
    }

    [HalconFact]
    public void EveryOperator_IsRegisteredUnderItsHalconName()
    {
        // 152 operators with a digit in their name, like affine_trans_point_3d, used to get a
        // name HDevelop doesn't know
        HOperatorSet.GetOperatorName("", out HTuple names);
        var config = new FishboneConfiguration().AddPlugin(new HalconOperatorPlugin());

        var missing = names.SArr.Where(name => !config.BuiltIns.ContainsKey(name)).ToList();

        Assert.Empty(missing);
        Assert.Contains("affine_trans_point_3d", config.BuiltIns.Keys);
        Assert.Contains("rgb1_to_gray", config.BuiltIns.Keys);
    }

    [HalconFact]
    public void Tuples_RoundTripThroughScriptValues()
    {
        var config = new FishboneConfiguration().AddPlugin(new HalconOperatorPlugin());

        var env = FishboneProgram.Run("""
            tuple_add([1, 2], 3, out sums);
            tuple_length([], out empty_length);
            tuple_concat([], [], out empty);
            tuple_add(0.5, 1, out half);
            """, config);

        // integers come back as long, several values as a list, and an empty tuple as null
        Assert.Equal(new List<object> { 4L, 5L }, env.GetValue("sums"));
        Assert.Equal(0L, env.GetValue("empty_length"));
        Assert.Null(env.GetValue("empty"));
        Assert.Equal(1.5, env.GetValue("half"));
    }
}
