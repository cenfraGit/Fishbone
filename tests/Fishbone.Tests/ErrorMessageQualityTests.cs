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