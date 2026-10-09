namespace Fishbone.Tests;

/// <summary>
/// <c>break</c> and <c>continue</c> only work inside a loop, and <c>return</c> only inside a
/// function. Anywhere else is a parse error, so the interpreter's internal signals never reach
/// the host or a loop outside the function.
/// </summary>
public class ControlFlowPlacementTests
{
    [Theory]
    [InlineData("return;", "return")]
    [InlineData("return 1;", "return")]
    [InlineData("break;", "break")]
    [InlineData("continue;", "continue")]
    [InlineData("if (true) { break; }", "break")]
    [InlineData("try { continue; } finally { }", "continue")]
    // a function body is outside any loop around the call
    [InlineData("func f() { break; }", "break")]
    [InlineData("while (true) { func f() { continue; } }", "continue")]
    public void OutsideItsConstruct_IsAParseError(string code, string keyword)
    {
        var error = Assert.Single(Assert.Throws<FishboneParseException>(() => FishboneProgram.Run(code)).Errors);

        Assert.Contains($"'{keyword}'", error.Message);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void BreakInAFunction_DoesNotStopTheCallersLoop()
    {
        // used to end the caller's while after one pass
        Assert.Throws<FishboneParseException>(() => FishboneProgram.Run("""
            let r = 0;
            func f() { break; }
            while (r < 5) { r += 1; f(); }
            """));
    }

    [Fact]
    public void InsideItsConstruct_RunsNormally()
    {
        var env = FishboneProgram.Run("""
            func firstOver(items, limit) {
                foreach (x in items) {
                    if (x <= limit) continue;
                    return x;
                }
                return null;
            }
            let hits = 0;
            for (i in 0, 10) {
                try { if (i == 3) break; } finally { hits += 1; }
            }
            let found = firstOver([1, 5, 9], 4);
            """);

        Assert.Equal(5, env.GetValue("found"));
        Assert.Equal(4, env.GetValue("hits"));
    }
}
