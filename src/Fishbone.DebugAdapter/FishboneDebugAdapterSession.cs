using System.Threading.Channels;
using Fishbone.Debugging;
using MediatR;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using DapThread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace Fishbone.DebugAdapter;

public sealed class FishboneDebugAdapterSession :
    IAttachHandler, IConfigurationDoneHandler, ISetBreakpointsHandler,
    IContinueHandler, INextHandler, IStepInHandler, IStepOutHandler, IPauseHandler,
    IThreadsHandler, IStackTraceHandler, IScopesHandler, IVariablesHandler,
    ISetExceptionBreakpointsHandler, IExceptionInfoHandler, IDisconnectHandler, ITerminateHandler,
    ILoadedSourcesHandler, ISourceHandler,
    IDisposable
{
    public const long ThreadId = 1;
    public const long SourceReference = 1;
    private readonly BreakpointCoordinator _coordinator;
    private readonly string _sourceIdentity;
    private readonly string _sourceCode;
    private readonly Source _source;
    private readonly int _lineCount;
    private readonly Func<CancellationToken, Task> _execute;
    private readonly CancellationTokenSource _executionCancellation = new();
    private readonly DebugSnapshotHandles _handles = new();
    private readonly Channel<IRequest> _events = Channel.CreateUnbounded<IRequest>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDebugAdapterServer? _server;
    private Task? _eventPump;
    private int _executionStarted;
    private int _entryStopPending;
    private bool _stopOnEntry;
    private volatile bool _detached;

    public FishboneDebugAdapterSession(
        BreakpointCoordinator coordinator,
        string sourcePath,
        int lineCount,
        Func<CancellationToken, Task> execute)
        : this(coordinator, sourcePath, Path.GetFileName(sourcePath), string.Empty, lineCount, execute)
    {
    }

    public FishboneDebugAdapterSession(
        BreakpointCoordinator coordinator,
        string sourceIdentity,
        string sourceName,
        string sourceCode,
        int lineCount,
        Func<CancellationToken, Task> execute)
    {
        _coordinator = coordinator;
        _sourceIdentity = sourceIdentity;
        _sourceCode = sourceCode;
        _source = new Source
        {
            Name = sourceName,
            Path = sourceIdentity,
            SourceReference = SourceReference,
            Origin = "Fishbone debug host"
        };
        _lineCount = lineCount;
        _execute = execute;
        _coordinator.Paused += OnPaused;
    }

    public Task<int> Completion => _completion.Task;
    public bool IsDetached => _detached;

    public void AttachServer(IDebugAdapterServer server, CancellationToken cancellationToken)
    {
        _server = server;
        _eventPump = PumpEventsAsync(cancellationToken);
    }

    public void WriteOutput(string text, bool isError = false) => Enqueue(new OutputEvent
    {
        Category = isError ? OutputEventCategory.StandardError : OutputEventCategory.StandardOutput,
        Output = text
    });

    public Task<AttachResponse> Handle(AttachRequestArguments request, CancellationToken cancellationToken)
    {
        if (request.ExtensionData.TryGetValue("stopOnEntry", out object? value))
            _stopOnEntry = value is bool boolean ? boolean : bool.TryParse(value?.ToString(), out bool parsed) && parsed;
        return Task.FromResult(new AttachResponse());
    }

    public Task<ConfigurationDoneResponse> Handle(ConfigurationDoneArguments request, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _executionStarted, 1) == 0)
        {
            if (_stopOnEntry)
            {
                Interlocked.Exchange(ref _entryStopPending, 1);
                _coordinator.Pause();
            }
            _ = Task.Run(RunExecutionAsync);
        }
        return Task.FromResult(new ConfigurationDoneResponse());
    }

    public Task<SetBreakpointsResponse> Handle(SetBreakpointsArguments request, CancellationToken cancellationToken)
    {
        bool sourceMatches = request.Source.SourceReference == SourceReference ||
            string.Equals(request.Source.Path, _sourceIdentity, StringComparison.OrdinalIgnoreCase);
        var requested = request.Breakpoints?.ToArray() ?? [];
        var accepted = new List<int>();
        var breakpoints = requested.Select(item =>
        {
            bool verified = sourceMatches && item.Line >= 1 && item.Line <= _lineCount;
            if (verified) accepted.Add(checked((int)item.Line));
            return new Breakpoint
            {
                Verified = verified,
                Line = item.Line,
                Source = _source,
                Message = verified ? null : "Breakpoint source or line is outside the active Fishbone script."
            };
        }).ToArray();
        _coordinator.ReplaceBreakpoints(accepted);
        return Task.FromResult(new SetBreakpointsResponse { Breakpoints = new Container<Breakpoint>(breakpoints) });
    }

    public Task<ContinueResponse> Handle(ContinueArguments request, CancellationToken cancellationToken)
    {
        Resume(_coordinator.Continue);
        return Task.FromResult(new ContinueResponse { AllThreadsContinued = true });
    }

    public Task<NextResponse> Handle(NextArguments request, CancellationToken cancellationToken)
    {
        Resume(_coordinator.StepOver);
        return Task.FromResult(new NextResponse());
    }

    public Task<StepInResponse> Handle(StepInArguments request, CancellationToken cancellationToken)
    {
        Resume(_coordinator.StepInto);
        return Task.FromResult(new StepInResponse());
    }

    public Task<StepOutResponse> Handle(StepOutArguments request, CancellationToken cancellationToken)
    {
        Resume(_coordinator.StepOut);
        return Task.FromResult(new StepOutResponse());
    }

    public Task<PauseResponse> Handle(PauseArguments request, CancellationToken cancellationToken)
    {
        _coordinator.Pause();
        return Task.FromResult(new PauseResponse());
    }

    public Task<ThreadsResponse> Handle(ThreadsArguments request, CancellationToken cancellationToken) =>
        Task.FromResult(new ThreadsResponse { Threads = new Container<DapThread>(new DapThread { Id = ThreadId, Name = "Fishbone Script" }) });

    public Task<LoadedSourcesResponse> Handle(LoadedSourcesArguments request, CancellationToken cancellationToken) =>
        Task.FromResult(new LoadedSourcesResponse { Sources = new Container<Source>(_source) });

    public Task<SourceResponse> Handle(SourceArguments request, CancellationToken cancellationToken)
    {
        bool matches = request.SourceReference == SourceReference || request.Source?.SourceReference == SourceReference;
        if (!matches)
            throw new InvalidOperationException("The requested source is not available in this debug session.");
        return Task.FromResult(new SourceResponse { Content = _sourceCode, MimeType = "text/plain" });
    }

    public Task<StackTraceResponse> Handle(StackTraceArguments request, CancellationToken cancellationToken)
    {
        var allFrames = _handles.GetFrames();
        IEnumerable<(long Id, DebugCallFrameSnapshot Frame)> selected = allFrames;
        if (request.StartFrame is > 0) selected = selected.Skip(checked((int)request.StartFrame.Value));
        if (request.Levels is > 0) selected = selected.Take(checked((int)request.Levels.Value));
        var frames = selected.Select(item => new StackFrame
        {
            Id = item.Id,
            Name = item.Frame.FunctionName,
            Source = _source,
            Line = item.Frame.Location.Line,
            Column = item.Frame.Location.Column
        });
        return Task.FromResult(new StackTraceResponse { StackFrames = new Container<StackFrame>(frames), TotalFrames = allFrames.Count });
    }

    public Task<ScopesResponse> Handle(ScopesArguments request, CancellationToken cancellationToken) =>
        Task.FromResult(new ScopesResponse { Scopes = new Container<Scope>(_handles.GetScopes(request.FrameId)) });

    public Task<VariablesResponse> Handle(VariablesArguments request, CancellationToken cancellationToken) =>
        Task.FromResult(new VariablesResponse
        {
            Variables = new Container<Variable>(_handles.GetVariables(request.VariablesReference, request.Start, request.Count))
        });

    public Task<SetExceptionBreakpointsResponse> Handle(SetExceptionBreakpointsArguments request, CancellationToken cancellationToken)
    {
        _coordinator.PauseOnRuntimeExceptions = request.Filters.Contains("all");
        return Task.FromResult(new SetExceptionBreakpointsResponse());
    }

    public Task<ExceptionInfoResponse> Handle(ExceptionInfoArguments request, CancellationToken cancellationToken)
    {
        var exception = _handles.GetException() ?? throw new InvalidOperationException("Execution is not paused on an exception.");
        return Task.FromResult(new ExceptionInfoResponse
        {
            ExceptionId = exception.Type,
            Description = exception.Message,
            BreakMode = ExceptionBreakMode.Always,
            Details = new ExceptionDetails { Message = exception.Message, TypeName = exception.Type }
        });
    }

    public Task<DisconnectResponse> Handle(DisconnectArguments request, CancellationToken cancellationToken)
    {
        if (request.TerminateDebuggee) Stop();
        else Detach();
        return Task.FromResult(new DisconnectResponse());
    }

    public Task<TerminateResponse> Handle(TerminateArguments request, CancellationToken cancellationToken)
    {
        Stop();
        return Task.FromResult(new TerminateResponse());
    }

    public void Stop()
    {
        _coordinator.Stop();
        _executionCancellation.Cancel();
        if (Interlocked.Exchange(ref _executionStarted, 1) == 0)
            _ = Task.Run(CompleteStoppedSessionAsync);
    }

    public void Detach()
    {
        _detached = true;
        _coordinator.ReplaceBreakpoints([]);
        _coordinator.PauseOnRuntimeExceptions = false;
        _coordinator.Continue();
        _handles.Clear();
        if (Interlocked.Exchange(ref _executionStarted, 1) == 0)
            _ = Task.Run(RunExecutionAsync);
    }

    private async Task RunExecutionAsync()
    {
        try
        {
            await _execute(_executionCancellation.Token).ConfigureAwait(false);
            if (!_detached)
            {
                Enqueue(new ExitedEvent { ExitCode = 0 });
                Enqueue(new TerminatedEvent());
            }
            await DrainEventsAsync().ConfigureAwait(false);
            _completion.TrySetResult(0);
        }
        catch (OperationCanceledException)
        {
            if (!_detached)
            {
                Enqueue(new ExitedEvent { ExitCode = 1 });
                Enqueue(new TerminatedEvent());
            }
            await DrainEventsAsync().ConfigureAwait(false);
            _completion.TrySetResult(1);
        }
        catch (Exception exception)
        {
            WriteOutput(exception.Message + Environment.NewLine, true);
            if (!_detached)
            {
                Enqueue(new ExitedEvent { ExitCode = 1 });
                Enqueue(new TerminatedEvent());
            }
            await DrainEventsAsync().ConfigureAwait(false);
            _completion.TrySetResult(1);
        }
    }

    private void OnPaused(object? sender, DebugPausedEventArgs args)
    {
        _handles.SetSnapshot(args.Snapshot);
        bool isEntryStop = Interlocked.Exchange(ref _entryStopPending, 0) == 1;
        Enqueue(new StoppedEvent
        {
            Reason = isEntryStop ? StoppedEventReason.Entry : args.Snapshot.Reason switch
            {
                DebugPauseReason.Breakpoint => StoppedEventReason.Breakpoint,
                DebugPauseReason.Step => StoppedEventReason.Step,
                DebugPauseReason.ManualPause => StoppedEventReason.Pause,
                DebugPauseReason.Exception => StoppedEventReason.Exception,
                _ => StoppedEventReason.Pause
            },
            Description = args.Snapshot.Exception?.Message,
            Text = args.Snapshot.Exception?.Type,
            ThreadId = ThreadId,
            AllThreadsStopped = true
        });
    }

    private void Resume(Action action)
    {
        if (_coordinator.State != DebugSessionState.Paused)
            return;

        action();
        if (!_coordinator.LastResumeWasSuccessful)
            return;

        _handles.Clear();
        Enqueue(new ContinuedEvent { ThreadId = ThreadId, AllThreadsContinued = true });
    }

    private void Enqueue(IRequest notification)
    {
        if (!_detached) _events.Writer.TryWrite(notification);
    }

    private async Task PumpEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var notification in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            _server?.SendNotification(notification);
    }

    private async Task DrainEventsAsync()
    {
        _events.Writer.TryComplete();
        if (_eventPump is not null)
#pragma warning disable VSTHRD003
            await _eventPump.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private async Task CompleteStoppedSessionAsync()
    {
        if (!_detached)
        {
            Enqueue(new ExitedEvent { ExitCode = 1 });
            Enqueue(new TerminatedEvent());
        }
        await DrainEventsAsync().ConfigureAwait(false);
        _completion.TrySetResult(1);
    }

    public void Dispose()
    {
        _coordinator.Paused -= OnPaused;
        _events.Writer.TryComplete();
        _executionCancellation.Dispose();
    }
}