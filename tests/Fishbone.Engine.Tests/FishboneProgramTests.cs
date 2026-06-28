using System.Text;

namespace Fishbone.Engine.Tests;

public class FishboneProgramTests
{
    [Fact]
    public void ParseSource_RunReusesParsedProgramWithDifferentData()
    {
        var program = FishboneProgram.ParseSource("let result = data * 2;");

        var first = program.Run(new FishboneConfiguration().AddValue("data", 5));
        var second = program.Run(new FishboneConfiguration().AddValue("data", 50));

        Assert.Equal(10, first.GetValue("result"));
        Assert.Equal(100, second.GetValue("result"));
    }

    [Fact]
    public void Run_ProducesIndependentEnvironmentsPerRun()
    {
        var program = FishboneProgram.ParseSource("let counter = seed; counter = counter + 1;");

        var first = program.Run(new FishboneConfiguration().AddValue("seed", 0));
        var second = program.Run(new FishboneConfiguration().AddValue("seed", 100));

        Assert.Equal(1, first.GetValue("counter"));
        Assert.Equal(101, second.GetValue("counter"));   // no leakage from the first run
    }

    [Fact]
    public void AddValue_SeedsVariableVisibleToScriptAndAmongLocalValues()
    {
        var program = FishboneProgram.ParseSource("let doubled = injected * 2;");
        var env = program.Run(new FishboneConfiguration().AddValue("injected", 21));

        Assert.Equal(42, env.GetValue("doubled"));
        // The seeded value lives in the variables dict (what the debugger surfaces), not built-ins.
        Assert.True(env.LocalValues.ContainsKey("injected"));
        Assert.Equal(21, env.LocalValues["injected"]);
    }

    [Fact]
    public void AddBuiltIn_IsReachableButNotAScriptVariable()
    {
        var program = FishboneProgram.ParseSource("let x = 1;");
        var env = program.Run(new FishboneConfiguration().AddBuiltIn("service", 99));

        Assert.Equal(99, env.GetValue("service"));          // reachable by name
        Assert.False(env.LocalValues.ContainsKey("service")); // but not a script variable
    }

    [Fact]
    public void AddValue_SeededVariableCanBeReassignedByScript()
    {
        var program = FishboneProgram.ParseSource("count = count + 5;");
        var env = program.Run(new FishboneConfiguration().AddValue("count", 10));

        Assert.Equal(15, env.GetValue("count"));
    }

    [Fact]
    public void ParseFile_ReadsUtf8AndUsesFileNameAsSourceName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fishbone_{Guid.NewGuid():N}.fb");
        File.WriteAllText(path, "let greeting = \"caña\";", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            var program = FishboneProgram.ParseFile(path);
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
        var a = FishboneProgram.ParseSource("let x = 1;");
        var b = FishboneProgram.ParseSource("let x = 1;");
        var c = FishboneProgram.ParseSource("let x = 2;");

        Assert.Equal(a.SourceIdentity, b.SourceIdentity);
        Assert.NotEqual(a.SourceIdentity, c.SourceIdentity);
    }
}
