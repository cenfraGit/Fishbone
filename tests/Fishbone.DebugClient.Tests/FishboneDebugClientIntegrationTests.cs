using Fishbone.DebugClient;

namespace Fishbone.DebugClient.Tests;

public class FishboneDebugClientIntegrationTests
{
    [Fact]
    public async Task SessionLaunchesHostAttachesInspectsAndContinues()
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}.fb");
        await File.WriteAllTextAsync(scriptPath, "let x = 1;\nprintln(x);\nx = x + 1;");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var session = new FishboneDebugClientSession(scriptPath, new FishboneDapHostLocator(AppContext.BaseDirectory));
        var paused = new TaskCompletionSource<FishbonePauseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        session.EventReceived += (_, debugEvent) =>
        {
            if (debugEvent is FishboneDebugPaused value) paused.TrySetResult(value.Snapshot);
            if (debugEvent is FishboneDebugTerminated) terminated.TrySetResult();
            if (debugEvent is FishboneDebugOutput valueOutput) output.Add(valueOutput.Text);
        };

        await session.StartAsync([2], timeout.Token);
        FishbonePauseSnapshot snapshot = await paused.Task.WaitAsync(timeout.Token);

        FishboneDebugScope visible = snapshot.Frames[0].Scopes.Single(scope => scope.Name == "Visible Variables");
        Assert.Contains(visible.Variables, variable => variable.Name == "x" && variable.Value == "1");
        await session.ContinueAsync(timeout.Token);
        await terminated.Task.WaitAsync(timeout.Token);
        Assert.Contains("1" + Environment.NewLine, output);
    }
}