using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111.Tests;

/// <summary>The shipped <c>halcon_images.fb</c> sample, run for real against HALCON's example images.</summary>
public class HalconImagesSampleTests
{
    [HalconFact]
    public void Sample_ReadsAndFiltersTheImages()
    {
        string script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "halcon_images.fb"));
        var config = new FishboneConfiguration()
            .AddBuiltIn("println", new Action<object?>(_ => { }))
            .AddPlugin(new HalconOperatorPlugin());

        var env = FishboneProgram.Run(script, config);

        Assert.True(Convert.ToInt32(env.GetValue("width")) > 0);
        Assert.Equal(3, Convert.ToInt32(env.GetValue("channels")));
        foreach (string image in new[] { "edges", "blurred", "real" })
            Assert.True(((HObject)env.GetValue(image)).IsInitialized(), image);
    }
}
