using Fishbone.Core;

namespace Fishbone.Interpreter.Tests;

public class FunctionOutRefParameterTests
{
    [Fact]
    public void Evaluate_OutParameter_IntroducesCallerVariable()
    {
        var env = InterpreterTestHelpers.Run("""
func produce(out result) { result = 42; }
produce(out answer);
""");

        Assert.Equal(42, env.GetValue("answer"));
    }

    [Fact]
    public void Evaluate_OutParameter_WritesThroughToExistingVariable()
    {
        var env = InterpreterTestHelpers.Run("""
func produce(out result) { result = 42; }
let answer = 5;
produce(out answer);
""");

        Assert.Equal(42, env.GetValue("answer"));
    }

    // an out parameter is seeded null and assigning it is not required
    [Fact]
    public void Evaluate_OutParameterNeverAssigned_YieldsNull()
    {
        var env = InterpreterTestHelpers.Run("""
func forgetful(out result) { }
forgetful(out answer);
""");

        Assert.Null(env.GetValue("answer"));
    }

    // the canonical "try" shape: a bool result plus the real value through 'out'
    [Fact]
    public void Evaluate_OutParameter_CombinesWithReturnValue()
    {
        var env = InterpreterTestHelpers.Run("""
func tryHalve(n, out half)
{
    if (n % 2 != 0) { return false; }
    half = n / 2;
    return true;
}
let evenOk = tryHalve(10, out evenHalf);
let oddOk = tryHalve(7, out oddHalf);
""");

        Assert.Equal(true, env.GetValue("evenOk"));
        Assert.Equal(5.0, env.GetValue("evenHalf"));

        // the early return leaves 'out' unassigned, so the caller gets null
        Assert.Equal(false, env.GetValue("oddOk"));
        Assert.Null(env.GetValue("oddHalf"));
    }

    [Fact]
    public void Evaluate_RefParameter_ReadsIncomingValueAndWritesBack()
    {
        var env = InterpreterTestHelpers.Run("""
func bump(ref n) { n = n + 1; }
let count = 10;
bump(ref count);
bump(ref count);
""");

        Assert.Equal(12, env.GetValue("count"));
    }

    [Fact]
    public void Evaluate_MixedDirections_BindEachIndependently()
    {
        var env = InterpreterTestHelpers.Run("""
func mix(byValue, out written, ref updated)
{
    written = byValue * 2;
    updated = updated + byValue;
    byValue = 999;
}
let start = 5;
let running = 100;
mix(start, out doubled, ref running);
""");

        Assert.Equal(10, env.GetValue("doubled"));
        Assert.Equal(105, env.GetValue("running"));
        // a by-value parameter reassigned inside the body must not touch the caller
        Assert.Equal(5, env.GetValue("start"));
    }

    // write-back only happens on a normal return, matching a throwing .NET method
    [Fact]
    public void Evaluate_BodyThrows_DoesNotWriteBackOutParameter()
    {
        var env = InterpreterTestHelpers.Run("""
func failing(out result)
{
    result = 42;
    throw "boom";
}
try { failing(out answer); } catch (e) { }
""");

        Assert.Throws<FishboneRuntimeException>(() => env.GetValue("answer"));
    }

    [Fact]
    public void Evaluate_RecursiveFunctionWithOutParameter_KeepsFramesIndependent()
    {
        var env = InterpreterTestHelpers.Run("""
func countdown(n, out total)
{
    if (n == 0) { total = 0; return; }
    countdown(n - 1, out inner);
    total = inner + n;
}
countdown(3, out sum);
""");

        Assert.Equal(6, env.GetValue("sum"));
    }

    [Fact]
    public void Evaluate_RepeatedCalls_DoNotShareState()
    {
        var env = InterpreterTestHelpers.Run("""
func twice(n, out doubled) { doubled = n * 2; }
twice(1, out first);
twice(50, out second);
""");

        Assert.Equal(2, env.GetValue("first"));
        Assert.Equal(100, env.GetValue("second"));
    }

    // Fishbone has no static checker, so aliasing one variable to two out params
    // resolves as "last write wins" rather than an error
    [Fact]
    public void Evaluate_SameVariableToTwoOutParameters_LastWriteWins()
    {
        var env = InterpreterTestHelpers.Run("""
func pair(out a, out b) { a = 1; b = 2; }
pair(out shared, out shared);
""");

        Assert.Equal(2, env.GetValue("shared"));
    }

    [Fact]
    public void Evaluate_FunctionReachedThroughVariable_StillBindsOutParameter()
    {
        var env = InterpreterTestHelpers.Run("""
func produce(out result) { result = 7; }
let alias = produce;
alias(out answer);
""");

        Assert.Equal(7, env.GetValue("answer"));
    }

    [Theory]
    // out parameter, no keyword at the call site
    [InlineData("func f(out a) { a = 1; } f(x);", "is an out parameter")]
    // out parameter, wrong keyword
    [InlineData("func f(out a) { a = 1; } let x = 1; f(ref x);", "is an out parameter")]
    // ref parameter, no keyword
    [InlineData("func f(ref a) { a = 1; } let x = 1; f(x);", "is a ref parameter")]
    // ref parameter, wrong keyword
    [InlineData("func f(ref a) { a = 1; } f(out x);", "is a ref parameter")]
    // keyword on a by-value parameter
    [InlineData("func f(a) { return a; } f(out x);", "passed by value")]
    // out/ref argument must be a plain variable
    [InlineData("func f(out a) { a = 1; } f(out 1 + 2);", "must be a variable")]
    [InlineData("func f(ref a) { a = 1; } f(ref 1 + 2);", "must be a variable")]
    // arity still checked
    [InlineData("func f(a, out b) { b = 1; } f(1);", "Expected 2 args")]
    public void Evaluate_ModifierMismatch_Throws(string code, string expectedMessage)
    {
        var exception = Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run(code));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void Evaluate_RefArgumentForUndefinedVariable_Throws()
    {
        var exception = Assert.ThrowsAny<Exception>(() => InterpreterTestHelpers.Run("""
func bump(ref n) { n = n + 1; }
bump(ref missing);
"""));

        Assert.Contains("missing", exception.Message);
    }
}
