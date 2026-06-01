namespace Fishbone.Engine.Tests;

public class FishboneConfigurationTests
{
    [Fact]
    public void RegisterVariable_AddsValueAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();

        var returned = config.RegisterVariable("answer", 42);

        Assert.Same(config, returned);
        Assert.Equal(42, config.Globals["answer"]);
    }

    [Fact]
    public void RegisterFunction_AddsDelegateAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();
        Func<int, int> doubleValue = value => value * 2;

        var returned = config.RegisterFunction("doubleValue", doubleValue);

        Assert.Same(config, returned);
        Assert.Same(doubleValue, config.Globals["doubleValue"]);
    }

    [Fact]
    public void Globals_UsesOrdinalKeyComparison()
    {
        var config = new FishboneConfiguration()
            .RegisterVariable("Name", 1)
            .RegisterVariable("name", 2);

        Assert.Equal(1, config.Globals["Name"]);
        Assert.Equal(2, config.Globals["name"]);
        Assert.Equal(2, config.Globals.Count);
    }
}
