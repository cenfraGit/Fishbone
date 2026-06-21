using Fishbone.Core;
using Fishbone.Debugging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;

namespace Fishbone.DebugAdapter.Tests;

public class SessionHandlerTests
{
    [Fact]
    public async Task ConfigurationDoneStartsExecutionExactlyOnce()
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        coordinator.OnExecutionStarted(new ProgramNode([]), new FishboneEnvironment());
        int executions = 0;
        using var session = new FishboneDebugAdapterSession(
            coordinator, "test.fb", 3, _ => { Interlocked.Increment(ref executions); return Task.CompletedTask; });

        Assert.Equal(0, executions);
        await session.Handle(new ConfigurationDoneArguments(), CancellationToken.None);
        await session.Handle(new ConfigurationDoneArguments(), CancellationToken.None);
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task SetBreakpointsValidatesSourceAndLines()
    {
        string path = Path.GetFullPath("test.fb");
        using var coordinator = new BreakpointCoordinator(path);
        using var session = new FishboneDebugAdapterSession(coordinator, path, 3, _ => Task.CompletedTask);

        var response = await session.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = path },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 2 }, new SourceBreakpoint { Line = 8 })
        }, CancellationToken.None);

        var breakpoints = response.Breakpoints.ToArray();
        Assert.True(breakpoints[0].Verified);
        Assert.False(breakpoints[1].Verified);
    }

    [Fact]
    public async Task ExceptionFilterControlsCoordinator()
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        using var session = new FishboneDebugAdapterSession(coordinator, "test.fb", 1, _ => Task.CompletedTask);

        await session.Handle(new SetExceptionBreakpointsArguments { Filters = new Container<string>() }, CancellationToken.None);
        Assert.False(coordinator.PauseOnRuntimeExceptions);
        await session.Handle(new SetExceptionBreakpointsArguments { Filters = new Container<string>("all") }, CancellationToken.None);
        Assert.True(coordinator.PauseOnRuntimeExceptions);
    }

    [Fact]
    public async Task NonTerminatingDisconnectRunsDetachedScript()
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        int executions = 0;
        using var session = new FishboneDebugAdapterSession(
            coordinator, "test.fb", 1, _ => { executions++; return Task.CompletedTask; });

        await session.Handle(new DisconnectArguments { TerminateDebuggee = false }, CancellationToken.None);

        Assert.Equal(0, await session.Completion.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(session.IsDetached);
        Assert.Equal(1, executions);
    }
}