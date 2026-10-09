namespace Fishbone.Tests;

public class ErrorMessageQualityTests
{
    [Fact]
    public void Run_ForeachOnNonIterable_IncludesTypeNameInMessage()
    {
        Exception exception = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("foreach (x in 42) { }", new FishboneConfiguration()));

        Assert.Contains("Int32", exception.Message);
        Assert.Contains("not iterable", exception.Message);
    }

    [Fact]
    public void Run_HostFunctionWithWrongArgumentCount_NamesWhatTheScriptCalled()
    {
        // a delegate is called through its Invoke and a method group through its .net name, and
        // the message used to name those
        var config = new FishboneConfiguration()
            .AddBuiltIn("add", new Func<int, int, int>((a, b) => a + b))
            .AddBuiltIn("absolute", new BoundMethod(null, typeof(Math).GetMethods().Where(m => m.Name == "Abs").ToList()));

        var delegateError = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("add(1, 2, 3);", config));
        var methodError = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("absolute(1, 2);", config));

        Assert.Equal("No overload of 'add' accepts 3 argument(s).", delegateError.Message);
        Assert.Equal("No overload of 'absolute' accepts 2 argument(s).", methodError.Message);
    }

    [Fact]
    public void Run_ForeachOnNull_SaysSo()
    {
        // used to surface the interpreter's own null reference error
        var exception = Assert.Throws<FishboneRuntimeException>(() => FishboneProgram.Run("let items = null;\nforeach (x in items) { }"));

        Assert.Equal("Cannot iterate over null.", exception.Message);
        Assert.Equal(2, exception.Line);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Run_CallingNull_IncludesClearGuidanceInMessage()
    {
        Exception exception = Assert.ThrowsAny<Exception>(() => FishboneProgram.Run("let x = null; x();", new FishboneConfiguration()));

        Assert.Contains("Cannot call null", exception.Message);
        Assert.Contains("functions", exception.Message);
    }
}