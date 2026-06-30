using Fishbone.Interpreter;

namespace Fishbone.Engine.Tests;

/// <summary>
/// Covers <see cref="INativeCallable"/>: a host can expose a callable that declares an in/out/ref
/// signature without being a reflected .NET method, and the interpreter binds inputs, converts them
/// through the registered-converter logic, and writes out/ref results back into script variables.
/// </summary>
public class NativeCallableTests
{
    // a flexible fake driven by a delegate so each test supplies its own signature and body
    private sealed class FakeNativeCallable : INativeCallable
    {
        private readonly Func<object?[], object?> _invoke;

        public FakeNativeCallable(IReadOnlyList<CallableParameter> parameters, Func<object?[], object?> invoke)
        {
            Parameters = parameters;
            _invoke = invoke;
        }

        public IReadOnlyList<CallableParameter> Parameters { get; }
        public object? Invoke(object?[] arguments) => _invoke(arguments);
    }

    // a wrapper that is deliberately not IConvertible
    private sealed class Boxed
    {
        public Boxed(int value) => Value = value;
        public int Value { get; }
    }

    // divmod(dividend, divisor, out quotient, out remainder): two inputs, two outputs
    private static FakeNativeCallable Divmod() => new(
        [
            new CallableParameter("dividend", typeof(int), ParameterDirection.In),
            new CallableParameter("divisor", typeof(int), ParameterDirection.In),
            new CallableParameter("quotient", typeof(int), ParameterDirection.Out),
            new CallableParameter("remainder", typeof(int), ParameterDirection.Out),
        ],
        args =>
        {
            int a = (int)args[0]!, b = (int)args[1]!;
            args[2] = a / b;
            args[3] = a % b;
            return null;
        });

    [Fact]
    public void OutParameters_DeclareScriptVariables()
    {
        var config = new FishboneConfiguration().AddBuiltIn("divmod", Divmod());

        var env = FishboneEngine.Run("divmod(17, 5, out q, out r);", config);

        Assert.Equal(3, env.GetValue("q"));
        Assert.Equal(2, env.GetValue("r"));
    }

    [Fact]
    public void RefParameter_ReadsAndWritesThroughExistingVariable()
    {
        var callable = new FakeNativeCallable(
            [new CallableParameter("value", typeof(int), ParameterDirection.Ref)],
            args => { args[0] = (int)args[0]! + 1; return null; });
        var config = new FishboneConfiguration().AddBuiltIn("bump", callable);

        var env = FishboneEngine.Run("let x = 10; bump(ref x); bump(ref x);", config);

        Assert.Equal(12, env.GetValue("x"));
    }

    [Fact]
    public void ReturnValue_FlowsBackToScript()
    {
        var callable = new FakeNativeCallable(
            [
                new CallableParameter("a", typeof(int), ParameterDirection.In),
                new CallableParameter("b", typeof(int), ParameterDirection.In),
            ],
            args => (int)args[0]! + (int)args[1]!);
        var config = new FishboneConfiguration().AddBuiltIn("add", callable);

        var env = FishboneEngine.Run("let s = add(2, 3);", config);

        Assert.Equal(5, env.GetValue("s"));
    }

    [Fact]
    public void OutValue_IsNormalizedByRegisteredFromConverter()
    {
        var callable = new FakeNativeCallable(
            [
                new CallableParameter("seed", typeof(int), ParameterDirection.In),
                new CallableParameter("result", typeof(Boxed), ParameterDirection.Out),
            ],
            args => { args[1] = new Boxed((int)args[0]! + 1); return null; });
        var config = new FishboneConfiguration()
            .AddTypeConverter(typeof(Boxed), v => new Boxed(Convert.ToInt32(v)), v => ((Boxed)v).Value)
            .AddBuiltIn("produce", callable);

        var env = FishboneEngine.Run("produce(41, out made);", config);

        Assert.Equal(42, env.GetValue("made"));
    }

    [Fact]
    public void OutParameter_RequiresOutKeyword()
    {
        var config = new FishboneConfiguration().AddBuiltIn("divmod", Divmod());

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("divmod(17, 5, q, r);", config));
    }

    [Fact]
    public void ByValueParameter_RejectsOutKeyword()
    {
        var callable = new FakeNativeCallable(
            [new CallableParameter("a", typeof(int), ParameterDirection.In)],
            args => args[0]);
        var config = new FishboneConfiguration().AddBuiltIn("echo", callable);

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let s = echo(out x);", config));
    }

    [Fact]
    public void WrongArgumentCount_IsRejected()
    {
        var config = new FishboneConfiguration().AddBuiltIn("divmod", Divmod());

        Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("divmod(17, 5, out q);", config));
    }
}