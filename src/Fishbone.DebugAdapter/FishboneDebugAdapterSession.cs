using Fishbone.Debugging;
using Fishbone.Parser;
using MediatR;
using OmniSharp.Extensions.DebugAdapter.Protocol.Events;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using OmniSharp.Extensions.DebugAdapter.Protocol.Requests;
using OmniSharp.Extensions.DebugAdapter.Protocol.Server;
using System.Threading.Channels;
using DapThread = OmniSharp.Extensions.DebugAdapter.Protocol.Models.Thread;

namespace Fishbone.DebugAdapter;

public sealed class FishboneDebugAdapterSession :
    IAttachHandler, IConfigurationDoneHandler, ISetBreakpointsHandler,
    IContinueHandler, INextHandler, IStepInHandler, IStepOutHandler, IPauseHandler,
    IThreadsHandler, IStackTraceHandler, IScopesHandler, IVariablesHandler,
    ISetExceptionBreakpointsHandler, IExceptionInfoHandler, IDisconnectHandler, ITerminateHandler,
    ILoadedSourcesHandler, ISourceHandler, IEvaluateHandler, ICompletionsHandler,
    IDisposable
{
    public const long ThreadId = 1;
    public const long SourceReference = 1;
    private readonly BreakpointCoordinator _coordinator;
    private readonly string _sourceIdentity;
    private readonly string _sourceCode;
    private readonly Source _source;
    private readonly int _lineCount;
    private readonly SortedSet<int>? _statementLines;
    private readonly Func<CancellationToken, Task> _execute;
    private readonly CancellationTokenSource _executionCancellation = new();
    private readonly DebugSnapshotHandles _handles;
    private readonly FishboneConfiguration? _configuration;
    // describing a configuration with a big plugin, like HALCON's thousands of operators, takes a
    // while, so the analysis starts in the background at the first pause, before a watch asks
    private Task<FishboneAnalysis>? _analysis;
    private readonly object _analysisStart = new();
    // a watch runs on the paused script's values, so the script doesn't resume until it's done
    private readonly object _evaluating = new();
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromSeconds(10);
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
        Func<CancellationToken, Task> execute,
        FishboneConfiguration? configuration = null)
    {
        _coordinator = coordinator;
        _handles = new DebugSnapshotHandles(configuration);
        _configuration = configuration;
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
        _statementLines = FindStatementLines(sourceCode);
        _execute = execute;
        _coordinator.Paused += OnPaused;
        _coordinator.Resumed += OnResumed;
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
        _stopOnEntry = ReadFlag(request, "stopOnEntry");
        _coordinator.PauseAtEnd = ReadFlag(request, "pauseAtEnd");
        return Task.FromResult(new AttachResponse());
    }

    private static bool ReadFlag(AttachRequestArguments request, string name) =>
        request.ExtensionData.TryGetValue(name, out object? value)
        && (value is bool boolean ? boolean : bool.TryParse(value?.ToString(), out bool parsed) && parsed);

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
            bool inScript = sourceMatches && item.Line >= 1 && item.Line <= _lineCount;
            int? bound = inScript ? BindLine(checked((int)item.Line)) : null;
            if (bound is not null) accepted.Add(bound.Value);
            return new Breakpoint
            {
                Verified = bound is not null,
                Line = bound ?? item.Line,
                Source = _source,
                Message = bound is not null ? null
                    : inScript ? "No statement starts on or after this line."
                    : "Breakpoint source or line is outside the active Fishbone script."
            };
        }).ToArray();
        _coordinator.ReplaceBreakpoints(accepted);
        return Task.FromResult(new SetBreakpointsResponse { Breakpoints = new Container<Breakpoint>(breakpoints) });
    }

    // like visual studio, a line with no statement (a brace, a comment, a blank line) binds to
    // the next line that starts one. without source to parse, a line binds as is
    private int? BindLine(int line)
    {
        if (_statementLines is null)
            return line;
        var next = _statementLines.GetViewBetween(line, int.MaxValue);
        return next.Count > 0 ? next.Min : null;
    }

    private static SortedSet<int>? FindStatementLines(string sourceCode)
    {
        if (string.IsNullOrEmpty(sourceCode))
            return null;
        try
        {
            return StatementLines.Find(ASTParser.Parse(sourceCode));
        }
        catch (FishboneParseException)
        {
            // the run reports the parse error itself
            return null;
        }
    }

    public Task<FishboneImageResponse> Handle(FishboneImageArguments request, CancellationToken cancellationToken) =>
        Task.FromResult(FishboneImageResponse.From(_handles.GetImage(request.VariablesReference)));

    // a watch. it's evaluated in the frame's scope, with the script's configuration, and comes
    // back like a variable, so it can have children or be an image. a watch that fails answers
    // with the reason, marked as a failed evaluation, since a failed request loses its message
    public Task<EvaluateResponse> Handle(EvaluateArguments request, CancellationToken cancellationToken)
    {
        lock (_evaluating)
        {
            Variable variable;
            try
            {
                variable = Evaluate(request.Expression, request.FrameId);
            }
            catch (Exception exception)
            {
                return Task.FromResult(new EvaluateResponse
                {
                    Result = exception switch
                    {
                        FishboneParseException { Errors.Count: > 0 } parse => $"column {parse.Errors[0].Column}: {parse.Errors[0].Message}",
                        OperationCanceledException => $"it took longer than {EvaluationTimeout.TotalSeconds:F0} seconds",
                        _ => exception.Message
                    },
                    PresentationHint = new VariablePresentationHint
                    {
                        Attributes = new Container<VariableAttributes>(new VariableAttributes(FailedEvaluation))
                    }
                });
            }
            return Task.FromResult(new EvaluateResponse
            {
                Result = variable.Value,
                Type = variable.Type,
                VariablesReference = variable.VariablesReference,
                PresentationHint = variable.PresentationHint,
                NamedVariables = variable.NamedVariables,
                IndexedVariables = variable.IndexedVariables
            });
        }
    }

    private Task<FishboneAnalysis> Analysis()
    {
        lock (_analysisStart)
            return _analysis ??= Task.Run(() => FishboneAnalysis.Analyze(_sourceCode, (_configuration ?? new FishboneConfiguration()).Describe()));
    }

    /// <summary>Marks a watch's answer as the reason it failed.</summary>
    public const string FailedEvaluation = "failedEvaluation";

    private Variable Evaluate(string expression, long? frameId)
    {
        if (_coordinator.State != DebugSessionState.Paused)
            throw new InvalidOperationException("Watches are evaluated while the script is paused.");
        FishboneEnvironment environment = _handles.GetFrame(frameId).Environment
            ?? throw new InvalidOperationException("The frame has no scope to evaluate in.");
        using var timeout = new CancellationTokenSource(EvaluationTimeout);
        object? value = FishboneExpression.Evaluate(expression, environment, _configuration, timeout.Token);
        return _handles.AddValue(expression, value);
    }

    // completions for a watch being typed, with the names in scope at the frame's line. after a
    // dot, the members of the value before it when its type isn't known from the script
    public async Task<CompletionsResponse> Handle(CompletionsArguments request, CancellationToken cancellationToken)
    {
        var none = new CompletionsResponse { Targets = new Container<CompletionItem>() };
        if (string.IsNullOrEmpty(_sourceCode))
            return none;
        FishboneAnalysis analysis = await Analysis().ConfigureAwait(false);
        // columns start at 1, like lines
        int caret = Math.Clamp((int)request.Column - 1, 0, request.Text.Length);
        FishboneCompletions? completions;
        lock (_evaluating)
        {
            if (_coordinator.State != DebugSessionState.Paused)
                return none;
            try
            {
                DebugCallFrameSnapshot frame = _handles.GetFrame(request.FrameId);
                completions = analysis.WatchCompletionsAt(_sourceCode, frame.Location.Line, request.Text, caret,
                    frame.Environment is { } environment ? expression => FishboneExpression.Evaluate(expression, environment, _configuration) : null);
            }
            catch (InvalidOperationException)
            {
                // the frame is gone, the script continued
                return none;
            }
        }
        if (completions is null)
            return none;

        var items = completions.Items.Select(item => new CompletionItem
        {
            Label = item.Text,
            Type = item.Kind switch
            {
                FishboneSuggestionKind.Keyword => CompletionItemType.Keyword,
                FishboneSuggestionKind.Function => CompletionItemType.Function,
                FishboneSuggestionKind.Type => CompletionItemType.Class,
                FishboneSuggestionKind.Method => CompletionItemType.Method,
                FishboneSuggestionKind.Property => CompletionItemType.Property,
                FishboneSuggestionKind.Field => CompletionItemType.Field,
                _ => CompletionItemType.Variable
            },
            Start = completions.Start + 1,
            Length = caret - completions.Start
        });
        return new CompletionsResponse { Targets = new Container<CompletionItem>(items) };
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
        // no one is left to resume a pause at the end, and the script would wait there forever
        _coordinator.PauseAtEnd = false;
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
        if (!string.IsNullOrEmpty(_sourceCode))
            _ = Analysis();
        bool isEntryStop = Interlocked.Exchange(ref _entryStopPending, 0) == 1;
        Enqueue(new StoppedEvent
        {
            Reason = isEntryStop ? StoppedEventReason.Entry : args.Snapshot.Reason switch
            {
                DebugPauseReason.Breakpoint => StoppedEventReason.Breakpoint,
                DebugPauseReason.Step => StoppedEventReason.Step,
                DebugPauseReason.ManualPause => StoppedEventReason.Pause,
                DebugPauseReason.Exception => StoppedEventReason.Exception,
                // final end-of-program pause: a custom reason the client/UI recognizes (see
                // FishbonePauseSnapshot.ProgramExitReason) so it finishes without an interactive stop
                DebugPauseReason.Completed => new StoppedEventReason("program-exit"),
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

        // the "continued" notification is emitted by OnResumed, which the coordinator raises
        // before it releases the interpreter, so it can never be overtaken by the next pause's
        // 'stopped' notification (the bug behind intermittent stepping hangs / dropped sessions).
        // a watch still running on the script's values finishes first
        lock (_evaluating)
            action();
    }

    private void OnResumed(object? sender, EventArgs e)
    {
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
        _coordinator.Resumed -= OnResumed;
        _events.Writer.TryComplete();
        _executionCancellation.Dispose();
    }
}