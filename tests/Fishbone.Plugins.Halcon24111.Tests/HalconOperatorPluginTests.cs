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

    // the pattern the plugin's README shows: one result is a plain value, so a loop goes by the
    // count and picks each one with tuple_select, which works for none, one or several
    [HalconFact]
    public void OneResultIsAValue_SoALoopGoesByTheCount()
    {
        HOperatorSet.GenCircle(out HObject one, 20, 20, 5);
        HOperatorSet.AreaCenter(one, out HTuple circleArea, out _, out _);
        var config = new FishboneConfiguration().AddPlugin(new HalconOperatorPlugin());

        foreach (int circles in new[] { 0, 1, 3 })
        {
            var env = FishboneProgram.Run($$"""
                gen_empty_obj(out regions);
                for (i in 0, {{circles}}) {
                    gen_circle(out circle, 20 + i * 30, 20, 5);
                    concat_obj(regions, circle, out regions);
                }
                area_center(regions, out areas, out rows, out cols);
                count_obj(regions, out n);
                let total = 0;
                for (i in 0, n) {
                    tuple_select(areas, i, out area);
                    total += area;
                }
                """, config);

            object? areas = env.GetValue("areas");
            switch (circles)
            {
                case 0: Assert.Null(areas); break;
                case 1: Assert.IsType<long>(areas); break;
                default: Assert.Equal(circles, Assert.IsType<List<object>>(areas).Count); break;
            }
            Assert.Equal(circles * circleArea.L, Convert.ToInt64(env.GetValue("total")));
        }
    }
}
