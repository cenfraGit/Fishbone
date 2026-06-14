namespace Fishbone.Engine.Tests;

public class DelegateBridgeTests
{
    [Fact]
    public void Run_CSharpDelegateBridge_InvokesAndMarshalsTypesCorrectly()
    {
        var config = new FishboneConfiguration()
            .RegisterFunction("formatValue", new Func<double, int, string>((val, precision) =>
                val.ToString($"F{precision}")));

        var env = FishboneEngine.Run("""
let scriptResult = formatValue(42, 3);
""", config);

        Assert.Equal("42.000", env.GetValue("scriptResult"));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_ReturnsPrimitiveValuesToScript()
    {
        var config = new FishboneConfiguration()
            .RegisterFunction("doubleValue", new Func<int, int>(value => value * 2));

        var env = FishboneEngine.Run("""
let scriptResult = doubleValue(21);
""", config);

        Assert.Equal(42, env.GetValue("scriptResult"));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_WithWrongArity_Throws()
    {
        var config = new FishboneConfiguration()
            .RegisterFunction("add", new Func<int, int, int>((left, right) => left + right));

        Assert.Throws<Exception>(() => FishboneEngine.Run("""
let scriptResult = add(1);
""", config));
    }

    [Fact]
    public void Run_CSharpDelegateBridge_PropagatesDelegateExceptions()
    {
        var config = new FishboneConfiguration()
            .RegisterFunction("explode", new Func<int>(() => throw new InvalidOperationException("boom")));

        var exception = Assert.Throws<InvalidOperationException>(() => FishboneEngine.Run("""
let scriptResult = explode();
""", config));

        Assert.Equal("boom", exception.Message);
    }
}
