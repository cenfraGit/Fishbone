namespace Fishbone.Engine.Tests;

public class OptionalParameterTests
{
    private sealed class Greeter
    {
        public string Join(string a, string b = "-", string c = "!") => $"{a}{b}{c}";

        // overloaded: one exact 1-arg form, one 2-arg form with an optional tail
        public int Pick(int a) => 1;
        public int Pick(int a, int b = 0) => 2;
    }

    private static FishboneConfiguration ConfigWithGreeter() =>
        new FishboneConfiguration().AddBuiltIn("g", new Greeter());

    [Fact]
    public void OmittingTrailingOptionals_FillsTheirDefaults()
    {
        var env = FishboneEngine.Run("let r = g.Join(\"x\");", ConfigWithGreeter());
        Assert.Equal("x-!", env.GetValue("r"));
    }

    [Fact]
    public void OmittingSomeOptionals_FillsOnlyTheMissingOnes()
    {
        var env = FishboneEngine.Run("let r = g.Join(\"x\", \"+\");", ConfigWithGreeter());
        Assert.Equal("x+!", env.GetValue("r"));
    }

    [Fact]
    public void SupplyingAllArguments_UsesNoDefaults()
    {
        var env = FishboneEngine.Run("let r = g.Join(\"x\", \"+\", \"?\");", ConfigWithGreeter());
        Assert.Equal("x+?", env.GetValue("r"));
    }

    [Fact]
    public void ExactArityOverload_IsPreferredOverDefaultFilledOne()
    {
        var env = FishboneEngine.Run("let r = g.Pick(7);", ConfigWithGreeter());
        Assert.Equal(1, env.GetValue("r"));
    }

    [Fact]
    public void TwoArguments_SelectTheTwoParameterOverload()
    {
        var env = FishboneEngine.Run("let r = g.Pick(7, 8);", ConfigWithGreeter());
        Assert.Equal(2, env.GetValue("r"));
    }

    [Fact]
    public void TooManyArguments_StillFails()
    {
        Assert.ThrowsAny<Exception>(() =>
            FishboneEngine.Run("let r = g.Join(\"a\", \"b\", \"c\", \"d\");", ConfigWithGreeter()));
    }

    [Fact]
    public void OmittingRequiredParameter_StillFails()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("g", new Greeter());

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let r = g.Join();", config));
    }
}