namespace Fishbone.Engine.Tests;

public class FishboneConfigurationTests
{
    [Fact]
    public void RegisterBuiltIn_AddsValueAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();

        var returned = config.RegisterBuiltIn("answer", 42);

        Assert.Same(config, returned);
        Assert.Equal(42, config.BuiltIns["answer"]);
    }

    [Fact]
    public void RegisterFunction_AddsDelegateAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();
        Func<int, int> doubleValue = value => value * 2;

        var returned = config.RegisterFunction("doubleValue", doubleValue);

        Assert.Same(config, returned);
        Assert.Same(doubleValue, config.BuiltIns["doubleValue"]);
    }

    [Fact]
    public void BuiltIns_UsesOrdinalKeyComparison()
    {
        var config = new FishboneConfiguration(injectDefaults: false)
            .RegisterBuiltIn("Name", 1)
            .RegisterBuiltIn("name", 2);

        Assert.Equal(1, config.BuiltIns["Name"]);
        Assert.Equal(2, config.BuiltIns["name"]);
        Assert.Equal(2, config.BuiltIns.Count);
    }
}
