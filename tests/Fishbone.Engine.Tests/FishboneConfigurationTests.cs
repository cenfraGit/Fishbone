namespace Fishbone.Engine.Tests;

public class FishboneConfigurationTests
{
    [Fact]
    public void RegisterBuiltIn_AddsValueAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();

        var returned = config.AddBuiltIn("answer", 42);

        Assert.Same(config, returned);
        Assert.Equal(42, config.BuiltIns["answer"]);
    }

    [Fact]
    public void RegisterFunction_AddsDelegateAndReturnsSameConfiguration()
    {
        var config = new FishboneConfiguration();
        Func<int, int> doubleValue = value => value * 2;

        var returned = config.AddFunction("doubleValue", doubleValue);

        Assert.Same(config, returned);
        Assert.Same(doubleValue, config.BuiltIns["doubleValue"]);
    }

    [Fact]
    public void BuiltIns_UsesOrdinalKeyComparison()
    {
        var config = new FishboneConfiguration(injectDefaults: false)
            .AddBuiltIn("Name", 1)
            .AddBuiltIn("name", 2);

        Assert.Equal(1, config.BuiltIns["Name"]);
        Assert.Equal(2, config.BuiltIns["name"]);
        Assert.Equal(2, config.BuiltIns.Count);
    }
}
