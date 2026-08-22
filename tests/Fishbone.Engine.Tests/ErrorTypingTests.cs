using Fishbone.Core;

namespace Fishbone.Engine.Tests;

public class ErrorTypingTests
{
    [Fact]
    public void Run_LanguageDiagnosedError_IsRuntimeExceptionWithLocationAndNoInner()
    {
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneEngine.Run(
            "let a = 1;\nlet b = missing;", new FishboneConfiguration()));

        Assert.Null(exception.InnerException);
        Assert.Equal(2, exception.Line);
        Assert.Contains("missing", exception.Message);
    }

    // a malformed 'for' header used to escape as a bare NullReferenceException from the AST
    // builder; every diagnosable script defect must surface as a Fishbone exception type
    [Fact]
    public void Run_ForWithoutEndBound_IsParseExceptionNotNullReference()
    {
        var exception = Record.Exception(
            () => FishboneEngine.Run("for (i in 5) { println(i); }", new FishboneConfiguration()));

        Assert.IsType<Fishbone.Parser.FishboneParseException>(exception);
    }

    [Fact]
    public void Run_HostDelegateThrow_IsRuntimeExceptionWrappingOriginal()
    {
        var config = new FishboneConfiguration()
            .AddBuiltIn("boom", new Action(() => throw new InvalidOperationException("host failure")));

        var exception = Assert.Throws<FishboneRuntimeException>(
            () => FishboneEngine.Run("boom();", config));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(1, exception.Line);
    }

    [Fact]
    public void Run_ScriptCatch_BindsLocatedRuntimeExceptionForLanguageErrors()
    {
        var env = FishboneEngine.Run("""
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
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneEngine.Run(
            "let a = 1;\nlet a = 2;", new FishboneConfiguration()));

        Assert.Null(exception.InnerException);
        Assert.Equal(2, exception.Line);
        Assert.Contains("already declared", exception.Message);
    }
}