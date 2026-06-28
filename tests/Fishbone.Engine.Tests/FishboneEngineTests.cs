namespace Fishbone.Engine.Tests;

public class FishboneEngineTests
{
    [Fact]
    public void Run_ParsesAndInterpretsScript_ReturningFinalRootEnvironment()
    {
        var env = FishboneEngine.Run("""
let x = 1;
x = x + 4;
let y = x * 2;
""", new FishboneConfiguration());

        Assert.Equal(5, env.GetValue("x"));
        Assert.Equal(10, env.GetValue("y"));
    }

    [Fact]
    public void Run_SeedsRegisteredVariablesForScriptUse()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("baseValue", 10);

        var env = FishboneEngine.Run("""
let result = baseValue + 5;
""", config);

        Assert.Equal(10, env.GetValue("baseValue"));
        Assert.Equal(15, env.GetValue("result"));
    }

    [Fact]
    public void Run_ReturnedEnvironmentRetainsTopLevelDeclarationsAndGlobals()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("globalValue", 3);

        var env = FishboneEngine.Run("""
let scriptValue = globalValue + 7;
""", config);

        Assert.Equal(3, env.GetValue("globalValue"));
        Assert.Equal(10, env.GetValue("scriptValue"));
    }

    [Fact]
    public void Run_DefaultGetKeyReadsDictionaryLiteralValues()
    {
        var env = FishboneEngine.Run("""
let values = {"name": "Fishbone", 7: "seven"};
let name = values["name"];
let number = values[7];
""", new FishboneConfiguration());

        Assert.Equal("Fishbone", env.GetValue("name"));
        Assert.Equal("seven", env.GetValue("number"));
    }
}
