using Fishbone;
using Fishbone.Ast;
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
    public async Task SetBreakpointsBindsToNextStatementLine()
    {
        // like visual studio: a breakpoint on a line with no statement (a brace, a comment, a
        // blank line) binds to the next statement. after the last statement it can't bind
        const string source = """
let c = [1, 2];
foreach (i in c)
{
    // print it
    println(i);
}

let done = true;
// end
""";
        using var coordinator = new BreakpointCoordinator("test.fb");
        using var session = new FishboneDebugAdapterSession(
            coordinator, "test.fb", "test.fb", source, 9, _ => Task.CompletedTask);

        var response = await session.Handle(new SetBreakpointsArguments
        {
            Source = new Source { Path = "test.fb" },
            Breakpoints = new Container<SourceBreakpoint>(
                new SourceBreakpoint { Line = 1 },
                new SourceBreakpoint { Line = 3 },
                new SourceBreakpoint { Line = 4 },
                new SourceBreakpoint { Line = 7 },
                new SourceBreakpoint { Line = 9 })
        }, CancellationToken.None);

        var breakpoints = response.Breakpoints.ToArray();
        Assert.Equal(new[] { 1, 5, 5, 8 }, breakpoints.Take(4).Select(breakpoint => breakpoint.Line!.Value).ToArray());
        Assert.All(breakpoints.Take(4), breakpoint => Assert.True(breakpoint.Verified));
        Assert.False(breakpoints[4].Verified);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AttachPauseAtEndControlsCoordinator(bool pauseAtEnd)
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        using var session = new FishboneDebugAdapterSession(coordinator, "test.fb", 1, _ => Task.CompletedTask);
        var request = new AttachRequestArguments();
        request.ExtensionData["pauseAtEnd"] = pauseAtEnd;

        await session.Handle(request, CancellationToken.None);

        Assert.Equal(pauseAtEnd, coordinator.PauseAtEnd);
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

    [Fact]
    public async Task Detaching_DropsThePauseAtTheEnd()
    {
        using var coordinator = new BreakpointCoordinator("test.fb");
        using var session = new FishboneDebugAdapterSession(coordinator, "test.fb", 1, _ => Task.CompletedTask);
        var attach = new AttachRequestArguments();
        attach.ExtensionData["pauseAtEnd"] = true;
        await session.Handle(attach, CancellationToken.None);

        session.Detach();

        Assert.False(coordinator.PauseAtEnd);
    }

    [Fact]
    public async Task ExposesSourceAndAcceptsBreakpointBySourceReference()
    {
        const string sourceCode = "let answer = 42;";
        using var coordinator = new BreakpointCoordinator("fishbone://embedded/test.fb");
        using var session = new FishboneDebugAdapterSession(
            coordinator, "fishbone://embedded/test.fb", "test.fb", sourceCode, 1, _ => Task.CompletedTask);

        LoadedSourcesResponse loaded = await session.Handle(new LoadedSourcesArguments(), CancellationToken.None);
        Source source = Assert.Single(loaded.Sources);
        Assert.Equal(FishboneDebugAdapterSession.SourceReference, source.SourceReference);
        SourceResponse content = await session.Handle(new SourceArguments
        {
            Source = source,
            SourceReference = source.SourceReference!.Value
        }, CancellationToken.None);
        SetBreakpointsResponse breakpoints = await session.Handle(new SetBreakpointsArguments
        {
            Source = new Source { SourceReference = source.SourceReference },
            Breakpoints = new Container<SourceBreakpoint>(new SourceBreakpoint { Line = 1 })
        }, CancellationToken.None);

        Assert.Equal(sourceCode, content.Content);
        Assert.True(Assert.Single(breakpoints.Breakpoints).Verified);
    }
}