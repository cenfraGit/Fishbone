namespace Fishbone.Engine.Tests;

public class CancellationTests
{
    [Fact]
    public void Run_WithCancelledToken_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FishboneEngine.Run("let value = 42;", new FishboneConfiguration(), cancellation.Token));
    }
}