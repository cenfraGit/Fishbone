namespace Fishbone.Tests;

public class OutRefArgumentTests
{
    private static FishboneConfiguration Config() =>
        new FishboneConfiguration().AddBuiltIn("sample", new ByRefSample());

    [Fact]
    public void Run_OutArgument_IntroducesNewVariable()
    {
        var env = FishboneProgram.Run("""
let ok = sample.TryGet("answer", out result);
""", Config());

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(42, env.GetValue("result"));
    }

    [Fact]
    public void Run_OutArgument_WritesThroughToExistingVariable()
    {
        var env = FishboneProgram.Run("""
let result = 5;
sample.TryGet("answer", out result);
""", Config());

        Assert.Equal(42, env.GetValue("result"));
    }

    [Fact]
    public void Run_RefArgument_UpdatesExistingVariable()
    {
        var env = FishboneProgram.Run("""
let value = 10;
sample.Increment(ref value);
""", Config());

        Assert.Equal(11, env.GetValue("value"));
    }

    [Fact]
    public void Run_RefArgument_OnUndefinedVariable_Throws()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("sample.Increment(ref missing);", Config()));

        Assert.Equal("Undefined variable 'missing'.", exception.Message);
    }

    [Fact]
    public void Run_ByRefParameter_WithoutKeyword_Throws()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("""
let value = 0;
sample.Increment(value);
""", Config()));

        Assert.Equal("Parameter 'value' is a ref parameter; pass the argument with 'ref'.", exception.Message);
    }

    [Fact]
    public void Run_ValueParameter_WithByRefKeyword_Throws()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("""
let value = 1;
let echoed = sample.Echo(out value);
""", Config()));

        Assert.Equal("Parameter 'value' is passed by value; remove 'out'.", exception.Message);
    }

    [Fact]
    public void Run_ByRefKeyword_OnFishboneFunction_Throws()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("""
func identity(a) { return a; }
let result = identity(out x);
""", Config()));

        Assert.Equal("'out' arguments are only supported when calling .NET methods.", exception.Message);
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