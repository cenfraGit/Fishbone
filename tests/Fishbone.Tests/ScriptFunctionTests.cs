namespace Fishbone.Tests;

/// <summary>Script functions as the spec describes them: first class, closures, parameters by value.</summary>
public class ScriptFunctionTests
{
    [Fact]
    public void Recursion_Works()
    {
        var env = FishboneProgram.Run("""
            func fact(n) {
                if (n <= 1) return 1;
                return n * fact(n - 1);
            }
            let result = fact(10);
            """);

        Assert.Equal(3628800, env.GetValue("result"));
    }

    [Fact]
    public void Closure_EachCallGetsItsOwnEnvironment()
    {
        var env = FishboneProgram.Run("""
            func counter() {
                let count = 0;
                func next() {
                    count = count + 1;
                    return count;
                }
                return next;
            }
            let a = counter();
            let b = counter();
            a();
            a();
            let fromA = a();
            let fromB = b();
            """);

        Assert.Equal(3, env.GetValue("fromA"));
        Assert.Equal(1, env.GetValue("fromB"));
    }

    [Fact]
    public void Functions_PassAndReturnLikeValues()
    {
        var env = FishboneProgram.Run("""
            func twice(f, x) { return f(f(x)); }
            func inc(x) { return x + 1; }
            let alias = inc;
            let result = twice(alias, 5);
            """);

        Assert.Equal(7, env.GetValue("result"));
    }

    [Fact]
    public void Parameter_ReassignedInside_LeavesTheCallersVariable()
    {
        var env = FishboneProgram.Run("""
            func reset(x) { x = 0; }
            let n = 5;
            reset(n);
            func none() { }
            let nothing = none();
            """);

        Assert.Equal(5, env.GetValue("n"));
        Assert.Null(env.GetValue("nothing"));
    }

    [Fact]
    public void Inner_Declaration_ShadowsWithoutChangingTheOuter()
    {
        var env = FishboneProgram.Run("let x = 1; { let x = 2; } let after = x;");

        Assert.Equal(1, env.GetValue("after"));
    }
}
