using Fishbone.Core;
using Fishbone.Interpreter;

namespace Fishbone.Engine.Tests;

// argument binding lives in three call paths (a fishbone function, a manual callable,
// and a reflected .NET method) and each used to phrase the same failure itself. the
// wording had drifted: one said "Expected 2 args but got 3." and another said
// "Expected 2 argument(s) but got 3." for the identical mistake.
//
// these tests compare the paths against each other rather than against a fixed string,
// so they fail if the wording diverges again without pinning the wording itself.
public class SharedErrorMessageTests
{
    // a manual callable with one by-value parameter, to reach that binding path
    private sealed class OneInParameter : IManualCallable
    {
        public IReadOnlyList<CallableParameter> Parameters { get; } =
            [new CallableParameter("value", typeof(int), ParameterDirection.In)];

        public object? Invoke(object?[] arguments) => arguments[0];
    }

    private sealed class OneOutParameter : IManualCallable
    {
        public IReadOnlyList<CallableParameter> Parameters { get; } =
            [new CallableParameter("value", typeof(int), ParameterDirection.Out)];

        public object? Invoke(object?[] arguments)
        {
            arguments[0] = 7;
            return null;
        }
    }

    private static string MessageFor(string code, Action<FishboneConfiguration>? setup = null)
    {
        var config = new FishboneConfiguration();
        setup?.Invoke(config);

        var exception = Record.Exception(() => FishboneEngine.Run(code, config));
        Assert.NotNull(exception);
        return Assert.Single(FishboneDiagnostics.From(exception)).Message;
    }

    [Fact]
    public void ArityMismatch_ReadsTheSameForAScriptFunctionAndAManualCallable()
    {
        var scriptFunction = MessageFor("func f(a) { } f(1, 2);");
        var manualCallable = MessageFor("m(1, 2);", config => config.AddBuiltIn("m", new OneInParameter()));

        Assert.Equal(scriptFunction, manualCallable);
        Assert.Equal("Expected 1 argument but got 2.", scriptFunction);
    }

    [Fact]
    public void ArityMessage_AgreesWithItselfOnPlurals()
    {
        Assert.Equal("Expected 1 argument but got 2.", MessageFor("func f(a) { } f(1, 2);"));
        Assert.Equal("Expected 2 arguments but got 1.", MessageFor("func f(a, b) { } f(1);"));
    }

    [Fact]
    public void MissingOutModifier_ReadsTheSameForBothPaths()
    {
        var scriptFunction = MessageFor("func f(out value) { value = 1; } let v = 0; f(v);");
        var manualCallable = MessageFor("let v = 0; m(v);",
            config => config.AddBuiltIn("m", new OneOutParameter()));

        Assert.Equal(scriptFunction, manualCallable);
        Assert.Contains("is an out parameter", scriptFunction);
        Assert.Contains("pass the argument with 'out'", scriptFunction);
    }

    [Fact]
    public void OutTargetMustBeVariable_ReadsTheSameForBothPaths()
    {
        var scriptFunction = MessageFor("func f(out value) { value = 1; } f(out 1 + 2);");
        var manualCallable = MessageFor("m(out 1 + 2);",
            config => config.AddBuiltIn("m", new OneOutParameter()));

        Assert.Equal(scriptFunction, manualCallable);
        Assert.Equal("Out argument 'value' must be a variable.", manualCallable);
    }

    [Fact]
    public void ModifierOnByValueParameter_ReadsTheSameForBothPaths()
    {
        var scriptFunction = MessageFor("func f(value) { } let v = 0; f(out v);");
        var manualCallable = MessageFor("let v = 0; m(out v);",
            config => config.AddBuiltIn("m", new OneInParameter()));

        Assert.Equal(scriptFunction, manualCallable);
        Assert.Contains("is passed by value; remove 'out'", scriptFunction);
    }

    // --------------------------------------------------------------------------------
    // wording consistency of the one-off messages that used to be outliers
    // --------------------------------------------------------------------------------

    [Theory]
    [InlineData("for (i in 0, 5, 0) { }")]
    [InlineData("let x = nope;")]
    [InlineData("func f(a) { } f(1, 2);")]
    [InlineData("let x = null; x.Length;")]
    public void EveryRuntimeMessage_IsASentence(string code)
    {
        var message = MessageFor(code);

        Assert.EndsWith(".", message);
        Assert.True(char.IsUpper(message[0]), $"expected a capitalized message, got: {message}");
        // the colon-prefixed and contraction styles that a few messages used to have
        Assert.DoesNotContain("For:", message);
        Assert.DoesNotContain("can't", message);
    }
}