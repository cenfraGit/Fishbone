namespace Fishbone.Engine.Tests;

public class ErrorMessageQualityTests
{
    [Fact]
    public void Run_ForeachOnNonIterable_IncludesTypeNameInMessage()
    {
        Exception exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("foreach (x in 42) { }", new FishboneConfiguration()));

        Assert.Contains("Int32", exception.Message);
        Assert.Contains("not iterable", exception.Message);
    }

    [Fact]
    public void Run_CallingNull_IncludesClearGuidanceInMessage()
    {
        Exception exception = Assert.ThrowsAny<Exception>(() => FishboneEngine.Run("let x = null; x();", new FishboneConfiguration()));

        Assert.Contains("Cannot call null", exception.Message);
        Assert.Contains("functions", exception.Message);
    }
}