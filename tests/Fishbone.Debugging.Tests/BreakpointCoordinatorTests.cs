using Fishbone.Core;
using Fishbone.Debugging;

namespace Fishbone.Debugging.Tests;

public class BreakpointCoordinatorTests
{
    [Fact]
    public async Task BreakpointParksUntilContinueAndPublishesOnce()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        coordinator.AddBreakpoint(2);
        var paused = new TaskCompletionSource<DebugPauseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int eventCount = 0;
        coordinator.Paused += (_, args) =>
        {
            Interlocked.Increment(ref eventCount);
            paused.TrySetResult(args.Snapshot);
        };

        var execution = Task.Run(() => coordinator.OnBeforeExecute(Node(2), environment));
        var snapshot = await paused.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(execution.IsCompleted);
        Assert.Equal(DebugSessionState.Paused, coordinator.State);
        Assert.Equal(DebugPauseReason.Breakpoint, snapshot.Reason);
        coordinator.Continue();
        await execution.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task ResumeFromPauseEventCannotMissWakeup()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        coordinator.AddBreakpoint(1);
        coordinator.Paused += (_, _) => coordinator.Continue();

        await Task.Run(() => coordinator.OnBeforeExecute(Node(1), environment))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(DebugSessionState.Running, coordinator.State);
    }

    [Fact]
    public async Task ManualPauseStopsAtNextExecutableLocation()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        coordinator.Pause();
        var paused = new TaskCompletionSource<DebugPauseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Paused += (_, args) => paused.SetResult(args.Snapshot);

        var execution = Task.Run(() => coordinator.OnBeforeExecute(Node(4), environment));
        var snapshot = await paused.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DebugPauseReason.ManualPause, snapshot.Reason);
        coordinator.Continue();
        await execution.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopUnblocksParkedExecution()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        coordinator.AddBreakpoint(1);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Paused += (_, _) => paused.SetResult();
        var execution = Task.Run(() => coordinator.OnBeforeExecute(Node(1), environment));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.Stop();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(DebugSessionState.Stopping, coordinator.State);
    }

    [Fact]
    public async Task CancellationUnblocksParkedExecution()
    {
        using var cancellation = new CancellationTokenSource();
        var environment = new FishboneEnvironment();
        using var coordinator = new BreakpointCoordinator("test.fb", cancellation.Token);
        coordinator.OnExecutionStarted(new ProgramNode([]), environment);
        coordinator.AddBreakpoint(1);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Paused += (_, _) => paused.SetResult();
        var execution = Task.Run(() => coordinator.OnBeforeExecute(Node(1), environment));
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await execution.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void StepIntoStopsOnNextLineAndSupportsEarlyResume()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        coordinator.AddBreakpoint(1);
        var lines = new List<int>();
        coordinator.Paused += (_, args) =>
        {
            lines.Add(args.Snapshot.Location.Line);
            if (lines.Count == 1) coordinator.StepInto();
            else coordinator.Continue();
        };

        coordinator.OnBeforeExecute(Node(1), environment);
        coordinator.OnBeforeExecute(Node(2), environment);

        Assert.Equal([1, 2], lines);
    }

    [Fact]
    public void StepOverSkipsDeeperFramesAndStepOutStopsShallower()
    {
        using var coordinator = CreateStartedCoordinator(out var root);
        var function = new FishboneEnvironment(root);
        coordinator.OnFunctionEnter("work", function);
        coordinator.AddBreakpoint(2);
        var lines = new List<int>();
        coordinator.Paused += (_, args) =>
        {
            lines.Add(args.Snapshot.Location.Line);
            if (lines.Count == 1) coordinator.StepOut();
            else coordinator.Continue();
        };

        coordinator.OnBeforeExecute(Node(2), function);
        coordinator.OnBeforeExecute(Node(3), function);
        coordinator.OnFunctionExit("work");
        coordinator.OnBeforeExecute(Node(8), root);

        Assert.Equal([2, 8], lines);
    }

    [Fact]
    public void SnapshotMergesNearestScopeAndDoesNotChangeAfterResume()
    {
        using var coordinator = CreateStartedCoordinator(out var root);
        root.Declare("shared", 1);
        root.Declare("rootOnly", "root");
        var child = new FishboneEnvironment(root);
        child.Declare("shared", 2);
        child.Declare("local", "before");
        coordinator.AddBreakpoint(5);
        DebugPauseSnapshot? snapshot = null;
        coordinator.Paused += (_, args) =>
        {
            snapshot = args.Snapshot;
            coordinator.Continue();
        };

        coordinator.OnBeforeExecute(Node(5), child);
        child.Assign("local", "after");

        Assert.Equal(2, snapshot!.VisibleVariables.Single(variable => variable.Name == "shared").Value);
        Assert.Equal("before", snapshot.VisibleVariables.Single(variable => variable.Name == "local").Value);
        Assert.Equal("root", snapshot.VisibleVariables.Single(variable => variable.Name == "rootOnly").Value);
        Assert.Equal("<script>", snapshot.CallStack.Single().FunctionName);
    }

    [Fact]
    public void ExceptionPauseOnlyAllowsContinue()
    {
        using var coordinator = CreateStartedCoordinator(out var environment);
        int pauses = 0;
        coordinator.Paused += (_, args) =>
        {
            pauses++;
            coordinator.StepInto();
            Assert.Equal(DebugSessionState.Paused, coordinator.State);
            coordinator.Continue();
        };

        coordinator.OnRuntimeException(new InvalidOperationException("failed"), Node(7), environment);

        Assert.Equal(1, pauses);
        Assert.Equal(DebugSessionState.Running, coordinator.State);
    }

    private static BreakpointCoordinator CreateStartedCoordinator(out FishboneEnvironment environment)
    {
        environment = new FishboneEnvironment();
        var coordinator = new BreakpointCoordinator("test.fb");
        coordinator.OnExecutionStarted(new ProgramNode([]), environment);
        return coordinator;
    }

    private static LiteralNode Node(int line) => new(0) { Line = line, Column = 1 };
}