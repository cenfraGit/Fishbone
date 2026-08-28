using Fishbone;

namespace Fishbone.Tests;

public class ErrorTypingTests
{
    [Fact]
    public void Run_LanguageDiagnosedError_IsRuntimeExceptionWithLocationAndNoInner()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(
            "let a = 1;\nlet b = missing;", new FishboneConfiguration()));

        Assert.Null(exception.InnerException);
        Assert.Equal(2, exception.Line);
        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void Run_HostDelegateThrow_IsRuntimeExceptionWrappingOriginal()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("boom", new Action(() => throw new InvalidOperationException("host failure")));

        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneProgram.Run("boom();", config));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(1, exception.Line);
    }

    [Fact]
    public void Run_ScriptCatch_BindsLocatedRuntimeExceptionForLanguageErrors()
    {
        var env = FishboneProgram.Run("""
let typeName = "";
let line = 0;
try
{
    let x = null;
    x.Anything();
}
catch (e)
{
    typeName = e.GetType().Name;
    line = e.Line;
}
""", new FishboneConfiguration());

        Assert.Equal(nameof(FishboneRuntimeException), env.GetValue("typeName"));
        Assert.Equal(6, env.GetValue("line"));
    }

    [Fact]
    public void Run_EnvironmentErrors_AreRuntimeExceptions()
    {
        // double declaration comes from FishboneEnvironment (Fishbone.Core) rather than
        // the interpreter, and must surface as the same exception type
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run(
            "let a = 1;\nlet a = 2;", new FishboneConfiguration()));

        Assert.Null(exception.InnerException);
        Assert.Equal(2, exception.Line);
        Assert.Contains("already declared", exception.Message);
    }
}