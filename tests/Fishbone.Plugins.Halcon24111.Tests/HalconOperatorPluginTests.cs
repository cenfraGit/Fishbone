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

    // handles collected with tuple_concat used to come back as strings once there were two of
    // them, so the operator they were passed to next rejected them
    [HalconFact]
    public void Handles_SurviveARoundTripThroughScriptValues()
    {
        var config = new FishboneConfiguration().AddPlugin(new HalconOperatorPlugin());

        foreach (int count in new[] { 1, 2, 3 })
        {
            var env = FishboneProgram.Run($$"""
                tuple_gen_const(0, 0, out dicts);
                for (i in 0, {{count}}) {
                    create_dict(out d);
                    set_dict_tuple(d, "k", i);
                    tuple_concat(dicts, d, out dicts);
                }
                let total = 0;
                for (i in 0, {{count}}) {
                    tuple_select(dicts, i, out one);
                    get_dict_tuple(one, "k", out k);
                    total += k;
                }
                """, config);

            Assert.Equal(count * (count - 1) / 2, Convert.ToInt32(env.GetValue("total")));
        }

        // several handles are a list, and each item is a handle an operator accepts
        var looped = FishboneProgram.Run("""
            create_dict(out a);
            create_dict(out b);
            set_dict_tuple(a, "k", 1);
            set_dict_tuple(b, "k", 2);
            tuple_concat(a, b, out dicts);
            let total = 0;
            foreach (one in dicts) {
                get_dict_tuple(one, "k", out k);
                total += k;
            }
            """, config);
        Assert.Equal(3, Convert.ToInt32(looped.GetValue("total")));
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
