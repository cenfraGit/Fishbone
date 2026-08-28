using SpineIDE.Services;

namespace SpineIDE.Tests;

public class ScriptInputRequestTests
{
    [Fact]
    public async Task Submit_ReleasesWaitingWorkerWithValue()
    {
        using var request = new ScriptInputRequest(CancellationToken.None);
        Task<string> waitingWorker = Task.Run(request.Wait);

        request.Submit("42");

        Assert.Equal("42", await waitingWorker);
    }

    [Fact]
    public async Task CancellationToken_ReleasesWaitingWorkerWithCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var request = new ScriptInputRequest(cancellation.Token);
        Task<string> waitingWorker = Task.Run(request.Wait);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waitingWorker);
    }

    [Fact]
    public async Task Cancel_ReleasesWaitingWorkerWithCancellation()
    {
        using var request = new ScriptInputRequest(CancellationToken.None);
        Task<string> waitingWorker = Task.Run(request.Wait);

        request.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await waitingWorker);
    }

    [Fact]
    public async Task SequentialRequests_ReturnTheirOwnValues()
    {
        using var first = new ScriptInputRequest(CancellationToken.None);
        Task<string> firstWorker = Task.Run(first.Wait);
        first.Submit("first");

        using var second = new ScriptInputRequest(CancellationToken.None);
        Task<string> secondWorker = Task.Run(second.Wait);
        second.Submit("second");

        Assert.Equal("first", await firstWorker);
        Assert.Equal("second", await secondWorker);
    }
}