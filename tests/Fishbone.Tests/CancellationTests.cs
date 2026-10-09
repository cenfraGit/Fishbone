namespace Fishbone.Tests;

public class CancellationTests
{
    [Fact]
    public void Run_WithCancelledToken_ThrowsOperationCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FishboneProgram.Run("let value = 42;", new FishboneConfiguration(), null, cancellation.Token));
    }

    [Theory]
    [InlineData("cancel(); while (true) { }")]
    // a script can't catch cancellation, or a host could never stop it
    [InlineData("try { cancel(); while (true) { } } catch (e) { } while (true) { }")]
    [InlineData("func spin() { while (true) { } } try { cancel(); spin(); } catch { }")]
    public async Task Run_CancelledWhileRunning_Stops(string code)
    {
        using var cancellation = new CancellationTokenSource();
        var config = new FishboneConfiguration().AddBuiltIn("cancel", new Action(cancellation.Cancel));

        var run = Task.Run(() => FishboneProgram.Run(code, config, null, cancellation.Token));

        // a run that doesn't stop fails with a timeout instead
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}