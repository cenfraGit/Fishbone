namespace Fishbone.Engine.Tests;

public class UnitTest1
{
    [Fact]
    public void Engine_CSharpDelegateBridge_InvokesAndMarshalsTypesCorrectly()
    {
        var config = new FishboneConfiguration()
            .RegisterFunction("formatValue", new Func<double, int, string>((val, precision) =>
                val.ToString($"F{precision}")));
        string code = @"
            let scriptResult = formatValue(42, 3);
        ";

        var finalEnv = FishboneEngine.Run(code, config);

        Assert.Equal("42.000", finalEnv.GetValue("scriptResult"));
    }
}
