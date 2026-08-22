using Fishbone.Core;

namespace Fishbone.Engine.Tests;

public class TryCatchTests
{
    [Fact]
    public void Run_CatchBindsRealNetExceptionFromDelegate()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("boom", new Action(() => throw new InvalidOperationException("delegate failed")));

        var env = FishboneEngine.Run("""
let message = "";
let typeName = "";
try { boom(); }
catch (e)
{
    message = e.Message;
    typeName = e.GetType().Name;
}
""", config);

        Assert.Equal("delegate failed", env.GetValue("message"));
        Assert.Equal("InvalidOperationException", env.GetValue("typeName"));
    }

    [Fact]
    public void Run_CatchHandlesRuntimeErrors_AndScriptContinues()
    {
        var env = FishboneEngine.Run("""
let handled = false;
try
{
    let x = null;
    x.Anything();
}
catch { handled = true; }
let after = "still running";
""", new FishboneConfiguration());

        Assert.Equal(true, env.GetValue("handled"));
        Assert.Equal("still running", env.GetValue("after"));
    }

    [Fact]
    public void Run_FinallyRuns_OnSuccessAndOnCaughtException()
    {
        var env = FishboneEngine.Run("""
let log = [];
try { log.Add("ok"); } finally { log.Add("f1"); }
try { throw "x"; } catch { log.Add("caught"); } finally { log.Add("f2"); }
""", new FishboneConfiguration());

        var log = Assert.IsType<List<object>>(env.GetValue("log"));
        Assert.Equal(["ok", "f1", "caught", "f2"], log);
    }

    [Fact]
    public void Run_TryFinallyWithoutCatch_RunsFinallyAndExceptionEscapes()
    {
        var log = new List<string>();
        var config = new FishboneConfiguration()
            .AddBuiltIn("record", new Action<string>(log.Add));

        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneEngine.Run("""
try { throw "escaping"; }
finally { record("cleanup"); }
""", config));

        Assert.Equal(["cleanup"], log);
        Assert.Contains("escaping", exception.Message);
    }

    [Fact]
    public void Run_ThrownNonExceptionValue_IsWrappedAndExposesValue()
    {
        var env = FishboneEngine.Run("""
let message = "";
let value = 0;
try { throw 42; }
catch (e)
{
    message = e.Message;
    value = e.Value;
}
""", new FishboneConfiguration());

        Assert.Equal("42", env.GetValue("message"));
        Assert.Equal(42, env.GetValue("value"));
    }

    [Fact]
    public void Run_BareThrow_RethrowsOriginalException()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("boom", new Action(() => throw new InvalidOperationException("original")));

        var env = FishboneEngine.Run("""
let outer = "";
try
{
    try { boom(); }
    catch { throw; }
}
catch (e) { outer = e.Message; }
""", config);

        Assert.Equal("original", env.GetValue("outer"));
    }

    [Fact]
    public void Run_BareThrowOutsideCatch_IsRuntimeError()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneEngine.Run("""throw;""", new FishboneConfiguration()));
        Assert.Contains("only valid inside a catch block", exception.Message);
    }

    [Fact]
    public void Run_ControlFlow_IsNotInterceptedByCatch_ButTriggersFinally()
    {
        var env = FishboneEngine.Run("""
let log = [];

func f()
{
    try { return "value"; }
    catch { log.Add("wrong: catch saw return"); }
    finally { log.Add("func finally"); }
}
let result = f();

for (i in 0, 5)
{
    try { if (i == 1) break; }
    catch { log.Add("wrong: catch saw break"); }
    finally { log.Add("loop finally"); }
}
""", new FishboneConfiguration());

        Assert.Equal("value", env.GetValue("result"));
        var log = Assert.IsType<List<object>>(env.GetValue("log"));
        Assert.Equal(["func finally", "loop finally", "loop finally"], log);
    }

    [Fact]
    public void Run_CatchBindingIsScopedToCatchBlock()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneEngine.Run("""
try { throw "x"; } catch (e) { }
let leaked = e;
""", new FishboneConfiguration()));

        Assert.Contains("Undefined variable", exception.Message);
    }
}