namespace Fishbone.Engine.Tests;

public class OutRefArgumentTests
{
    private static FishboneConfiguration Config() =>
        new FishboneConfiguration().AddBuiltIn("sample", new ByRefSample());

    [Fact]
    public void Run_OutArgument_IntroducesNewVariable()
    {
        var env = FishboneEngine.Run("""
let ok = sample.TryGet("answer", out result);
""", Config());

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(42, env.GetValue("result"));
    }

    [Fact]
    public void Run_OutArgument_WritesThroughToExistingVariable()
    {
        var env = FishboneEngine.Run("""
let result = 5;
sample.TryGet("answer", out result);
""", Config());

        Assert.Equal(42, env.GetValue("result"));
    }

    [Fact]
    public void Run_RefArgument_UpdatesExistingVariable()
    {
        var env = FishboneEngine.Run("""
let value = 10;
sample.Increment(ref value);
""", Config());

        Assert.Equal(11, env.GetValue("value"));
    }

    [Fact]
    public void Run_RefArgument_OnUndefinedVariable_Throws()
    {
        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("sample.Increment(ref missing);", Config()));
    }

    [Fact]
    public void Run_ByRefParameter_WithoutKeyword_Throws()
    {
        var exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("""
let value = 0;
sample.Increment(value);
""", Config()));

        Assert.Contains("ref", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_ValueParameter_WithByRefKeyword_Throws()
    {
        var exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("""
let value = 1;
let echoed = sample.Echo(out value);
""", Config()));

        Assert.Contains("value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Run_ByRefKeyword_OnFishboneFunction_Throws()
    {
        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("""
func identity(a) { return a; }
let result = identity(out x);
""", Config()));
    }

    private sealed class ByRefSample
    {
        public bool TryGet(string key, out int value)
        {
            value = key == "answer" ? 42 : 0;
            return key == "answer";
        }

        public void Increment(ref int value)
        {
            value++;
        }

        public int Echo(int value) => value;
    }
}