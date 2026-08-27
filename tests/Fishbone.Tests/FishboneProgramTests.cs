using System.Text;

namespace Fishbone.Tests;

public class FishboneProgramTests
{
    [Fact]
    public void FromSourceCode_RunReusesParsedProgramWithDifferentData()
    {
        var program = FishboneProgram.FromSourceCode("let result = data * 2;");

        var first = program.Run(new FishboneConfiguration().AddValue("data", 5));
        var second = program.Run(new FishboneConfiguration().AddValue("data", 50));

        Assert.Equal(10, first.GetValue("result"));
        Assert.Equal(100, second.GetValue("result"));
    }

    [Fact]
    public void Run_ProducesIndependentEnvironmentsPerRun()
    {
        var program = FishboneProgram.FromSourceCode("let counter = seed; counter = counter + 1;");

        var first = program.Run(new FishboneConfiguration().AddValue("seed", 0));
        var second = program.Run(new FishboneConfiguration().AddValue("seed", 100));

        Assert.Equal(1, first.GetValue("counter"));
        Assert.Equal(101, second.GetValue("counter"));   // no leakage from the first run
    }

    [Fact]
    public void AddValue_SeedsVariableVisibleToScriptAndAmongLocalValues()
    {
        var program = FishboneProgram.FromSourceCode("let doubled = injected * 2;");
        var env = program.Run(new FishboneConfiguration().AddValue("injected", 21));

        Assert.Equal(42, env.GetValue("doubled"));
        // The seeded value lives in the variables dict (what the debugger surfaces), not built-ins.
        Assert.True(env.Values.ContainsKey("injected"));
        Assert.Equal(21, env.Values["injected"]);
    }

    [Fact]
    public void AddBuiltIn_IsReachableButNotAScriptVariable()
    {
        var program = FishboneProgram.FromSourceCode("let x = 1;");
        var env = program.Run(new FishboneConfiguration().AddBuiltIn("service", 99));

        Assert.Equal(99, env.GetValue("service"));          // reachable by name
        Assert.False(env.Values.ContainsKey("service")); // but not a script variable
    }

    [Fact]
    public void AddValue_SeededVariableCanBeReassignedByScript()
    {
        var program = FishboneProgram.FromSourceCode("count = count + 5;");
        var env = program.Run(new FishboneConfiguration().AddValue("count", 10));

        Assert.Equal(15, env.GetValue("count"));
    }

    [Fact]
    public void FromFile_ReadsUtf8AndUsesFileNameAsSourceName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fishbone_{Guid.NewGuid():N}.fb");
        File.WriteAllText(path, "let greeting = \"caña\";", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var program = FishboneProgram.FromFile(path);
            var env = program.Run(new FishboneConfiguration());

            Assert.Equal("caña", env.GetValue("greeting"));
            Assert.Equal(Path.GetFileName(path), program.SourceName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SourceIdentity_IsStableForSameSourceAndDiffersOtherwise()
    {
        var a = FishboneProgram.FromSourceCode("let x = 1;");
        var b = FishboneProgram.FromSourceCode("let x = 1;");
        var c = FishboneProgram.FromSourceCode("let x = 2;");

        Assert.Equal(a.SourceIdentity, b.SourceIdentity);
        Assert.NotEqual(a.SourceIdentity, c.SourceIdentity);
    }

    [Fact]
    public void Run_ParsesAndInterpretsScript_ReturningFinalRootEnvironment()
    {
        var env = FishboneProgram.Run("""
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

        var env = FishboneProgram.Run("""
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

        var env = FishboneProgram.Run("""
let scriptValue = globalValue + 7;
""", config);

        Assert.Equal(3, env.GetValue("globalValue"));
        Assert.Equal(10, env.GetValue("scriptValue"));
    }

    [Fact]
    public void Run_DefaultGetKeyReadsDictionaryLiteralValues()
    {
        var env = FishboneProgram.Run("""
let values = {"name": "Fishbone", 7: "seven"};
let name = values["name"];
let number = values[7];
""", new FishboneConfiguration());

        Assert.Equal("Fishbone", env.GetValue("name"));
        Assert.Equal("seven", env.GetValue("number"));
    }
}
