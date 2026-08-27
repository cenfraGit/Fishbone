namespace Fishbone.Tests;

public class DelegateBridgeTests
{
    [Fact]
    public void Run_CSharpDelegateBridge_InvokesAndMarshalsTypesCorrectly()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("formatValue", new Func<double, int, string>((val, precision) =>
                val.ToString($"F{precision}")));

        var env = FishboneProgram.Run("""
let scriptResult = formatValue(42, 3);
""", config);

        Assert.Equal("42.000", env.GetValue("scriptResult"));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_ReturnsPrimitiveValuesToScript()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("doubleValue", new Func<int, int>(value => value * 2));

        var env = FishboneProgram.Run("""
let scriptResult = doubleValue(21);
""", config);

        Assert.Equal(42, env.GetValue("scriptResult"));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_WithWrongArity_Throws()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("add", new Func<int, int, int>((left, right) => left + right));

        Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("""
let scriptResult = add(1);
""", config));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_PropagatesDelegateExceptions()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("explode", new Func<int>(() => throw new InvalidOperationException("boom")));

        var exception = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("""
let scriptResult = explode();
""", config));

        Assert.Equal("boom", exception.Message);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Run_CSharpDelegateBridge_AssignsOutAndRefIdentifierArguments()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("tryParse", new TryParseDelegate(TryParse))
            .AddBuiltIn("increment", new IncrementDelegate(Increment));

        var env = FishboneProgram.Run("""
let ok = tryParse("42", out parsed);
let value = 10;
increment(ref value);
""", config);

        Assert.Equal(true, env.GetValue("ok"));
        // `out parsed` introduces the variable; no prior declaration was needed.
        Assert.Equal(42, env.GetValue("parsed"));
        Assert.Equal(11, env.GetValue("value"));
    }

    private delegate bool TryParseDelegate(string text, out int value);
    private delegate void IncrementDelegate(ref int value);

    private static bool TryParse(string text, out int value)
    {
        return int.TryParse(text, out value);
    }

    private static void Increment(ref int value)
    {
        value++;
    }
}