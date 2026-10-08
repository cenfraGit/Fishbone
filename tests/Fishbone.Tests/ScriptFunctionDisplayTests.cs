using Fishbone.Debugging;

namespace Fishbone.Tests;

public class ScriptFunctionDisplayTests
{
    [Fact]
    public void ScriptFunction_ShowsItsSignature()
    {
        // variable views show this instead of the interpreter's type name
        var env = FishboneProgram.Run("""
func square(x) { return x * x; }
func add(a, b) { return a + b; }
func none() { return 0; }
""", new FishboneConfiguration());

        Assert.Equal("func square(x)", DebugValueFormatter.FormatValue(env.GetValue("square")));
        Assert.Equal("func add(a, b)", DebugValueFormatter.FormatValue(env.GetValue("add")));
        Assert.Equal("func none()", DebugValueFormatter.FormatValue(env.GetValue("none")));
    }
}
