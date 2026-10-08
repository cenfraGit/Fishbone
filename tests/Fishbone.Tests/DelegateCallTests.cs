using System.Linq.Expressions;

namespace Fishbone.Tests;

/// <summary>
/// Calling a .NET delegate from a script calls the delegate itself, so it behaves exactly like
/// calling it from C#: every handler of a multicast delegate runs, and delegates over extension
/// methods or compiled expressions get the arguments the caller sees.
/// </summary>
public class DelegateCallTests
{
    private delegate bool TryLookup(string key, out int value);
    private delegate void Doubler(ref int value);
    private delegate int AddWithDefault(int a, int b = 10);

    [Fact]
    public void Run_MulticastDelegate_RunsEveryHandler()
    {
        var log = new List<string>();
        Action<string> notify = x => log.Add("first " + x);
        notify += x => log.Add("second " + x);
        var config = new FishboneConfiguration().AddBuiltIn("notify", notify);

        FishboneProgram.Run("""notify("x");""", config);

        Assert.Equal(["first x", "second x"], log);
    }

    [Fact]
    public void Run_DelegateOverExtensionMethod_UsesBoundTarget()
    {
        // the list is the extension method's first argument, held by the delegate
        var list = new List<int> { 1, 2, 3 };
        var config = new FishboneConfiguration().AddBuiltIn("total", new Func<int>(list.Sum));

        var env = FishboneProgram.Run("let r = total();", config);

        Assert.Equal(6, env.GetValue("r"));
    }

    [Fact]
    public void Run_CompiledExpressionDelegate_TakesCallerArguments()
    {
        var x = Expression.Parameter(typeof(int), "x");
        var twice = Expression.Lambda<Func<int, int>>(Expression.Multiply(x, Expression.Constant(2)), x).Compile();
        var config = new FishboneConfiguration().AddBuiltIn("twice", twice);

        var env = FishboneProgram.Run("let r = twice(4);", config);

        Assert.Equal(8, env.GetValue("r"));
    }

    [Fact]
    public void Run_DelegateTypeWithOptionalParameter_UsesDefault()
    {
        // the default lives on the delegate type, not on the lambda
        var config = new FishboneConfiguration()
            .AddBuiltIn("add", new AddWithDefault((a, b) => a + b));

        var env = FishboneProgram.Run("""
let withDefault = add(1);
let withBoth = add(1, 2);
""", config);

        Assert.Equal(11, env.GetValue("withDefault"));
        Assert.Equal(3, env.GetValue("withBoth"));
    }

    [Fact]
    public void Run_DelegateWithOutAndRef_WritesBack()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("tryLookup", new TryLookup((string key, out int value) =>
            {
                value = key.Length;
                return true;
            }))
            .AddBuiltIn("doubleIt", new Doubler((ref int value) => value *= 2));

        var env = FishboneProgram.Run("""
let ok = tryLookup("abc", out length);
let n = 5;
doubleIt(ref n);
""", config);

        Assert.Equal(true, env.GetValue("ok"));
        Assert.Equal(3, env.GetValue("length"));
        Assert.Equal(10, env.GetValue("n"));
    }

    [Fact]
    public void Run_DelegateReturnedFromDotNet_CanBeCalled()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("makeAdder", new Func<int, Func<int, int>>(a => b => a + b));

        var env = FishboneProgram.Run("""
let addTwo = makeAdder(2);
let r = addTwo(3);
""", config);

        Assert.Equal(5, env.GetValue("r"));
    }

    [Fact]
    public void Run_DelegateConversionError_NamesLambdaParameter()
    {
        // the delegate type's Invoke calls its parameter 'arg'. the error should still use
        // the name the host wrote
        var config = new FishboneConfiguration()
            .AddBuiltIn("takeInt", new Func<int, int>(count => count));

        var exception = Assert.Throws<FishboneRuntimeException>(() =>
            FishboneProgram.Run("""let r = takeInt("x");""", config));

        Assert.Contains("'count'", exception.Message);
    }
}