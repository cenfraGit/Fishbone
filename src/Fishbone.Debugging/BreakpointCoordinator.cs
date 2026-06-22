using System.Collections.Immutable;
using Fishbone.Core;

namespace Fishbone.Debugging;

public sealed class BreakpointCoordinator : IFishboneDebugger, IDisposable
{
    private enum StepMode { None, Into, Over, Out }
    private sealed record Frame(string Name, FishboneEnvironment Environment)
    {
        public DebugSourceLocation? Location { get; set; }
    }

    private readonly object _sync = new();
    private readonly ManualResetEventSlim _threadGate = new(true);
    private readonly HashSet<int> _breakpoints = [];
    private readonly List<Frame> _frames = [];
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _cancellationRegistration;
    private DebugSessionState _state = DebugSessionState.Running;
    private StepMode _stepMode;
    private bool _pauseRequested;
    private bool _stopRequested;
    private bool _exceptionPause;
    private (int Line, int Depth)? _lastExecutableLocation;
    private (int Line, int Depth)? _resumeLocation;
    private int _targetDepth;
    private bool _pauseOnRuntimeExceptions = true;
    private bool _lastResumeWasSuccessful;

    public BreakpointCoordinator(string sourceId, CancellationToken cancellationToken = default)
    {
        SourceId = sourceId;
        _cancellationToken = cancellationToken;
        _cancellationRegistration = cancellationToken.Register(_threadGate.Set);
    }

    public string SourceId { get; }

    public DebugSessionState State
    {
        get { lock (_sync) return _state; }
    }

    public bool LastResumeWasSuccessful
    {
        get { lock (_sync) return _lastResumeWasSuccessful; }
    }

    public bool PauseOnRuntimeExceptions
    {
        get { lock (_sync) return _pauseOnRuntimeExceptions; }
        set { lock (_sync) _pauseOnRuntimeExceptions = value; }
    }

    public event EventHandler<DebugStateChangedEventArgs>? StateChanged;
    public event EventHandler<DebugPausedEventArgs>? Paused;

    public void AddBreakpoint(int line)
    {
        if (line <= 0) throw new ArgumentOutOfRangeException(nameof(line));
        lock (_sync) _breakpoints.Add(line);
    }

    public void RemoveBreakpoint(int line)
    {
        lock (_sync) _breakpoints.Remove(line);
    }

    public void ReplaceBreakpoints(IEnumerable<int> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var validated = lines.Distinct().ToArray();
        if (validated.Any(line => line <= 0))
            throw new ArgumentOutOfRangeException(nameof(lines));

        lock (_sync)
        {
            _breakpoints.Clear();
            _breakpoints.UnionWith(validated);
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_state == DebugSessionState.Running)
                _pauseRequested = true;
        }
    }

    public void Continue() => Resume(StepMode.None);
    public void StepInto() => Resume(StepMode.Into);
    public void StepOver() => Resume(StepMode.Over);
    public void StepOut() => Resume(StepMode.Out);

    public void Stop()
    {
        DebugStateChangedEventArgs? stateChange = null;
        lock (_sync)
        {
            if (_state is DebugSessionState.Completed or DebugSessionState.Stopping)
                return;

            _stopRequested = true;
            stateChange = SetStateLocked(DebugSessionState.Stopping);
            _threadGate.Set();
        }
        PublishStateChange(stateChange);
    }

    public void OnExecutionStarted(AstNode root, FishboneEnvironment environment)
    {
        lock (_sync)
        {
            _frames.Clear();
            _frames.Add(new Frame("<script>", environment));
            _lastExecutableLocation = null;
            _exceptionPause = false;
        }
    }

    public void OnBeforeExecute(AstNode node, FishboneEnvironment environment)
    {
        _cancellationToken.ThrowIfCancellationRequested();

        DebugPauseReason? reason = null;
        DebugPauseSnapshot? snapshot = null;
        DebugStateChangedEventArgs? stateChange = null;

        lock (_sync)
        {
            ThrowIfStoppedLocked();
            if (node is ProgramNode or BlockNode || node.Line <= 0)
                return;

            var location = (node.Line, _frames.Count - 1);
            _frames[^1].Location = new DebugSourceLocation(SourceId, node.Line, node.Column);
            var isNewLocation = _lastExecutableLocation != location;
            _lastExecutableLocation = location;
            if (!isNewLocation)
                return;

            if (_pauseRequested)
                reason = DebugPauseReason.ManualPause;
            else if (_breakpoints.Contains(node.Line))
                reason = DebugPauseReason.Breakpoint;
            else if (ShouldPauseForStepLocked(location))
                reason = DebugPauseReason.Step;

            if (reason is null)
                return;

            _pauseRequested = false;
            _exceptionPause = false;
            _stepMode = StepMode.None;
            _resumeLocation = location;
            snapshot = CreateSnapshotLocked(node, environment, reason.Value, null);
            _threadGate.Reset();
            stateChange = SetStateLocked(DebugSessionState.Paused);
        }

        PublishStateChange(stateChange);
        Paused?.Invoke(this, new DebugPausedEventArgs(snapshot));
        WaitForResume();
    }

    public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment)
    {
        DebugPauseSnapshot? snapshot;
        DebugStateChangedEventArgs? stateChange;
        lock (_sync)
        {
            ThrowIfStoppedLocked();
            if (!_pauseOnRuntimeExceptions)
                return;

            if (_frames.Count > 0 && node.Line > 0)
                _frames[^1].Location = new DebugSourceLocation(SourceId, node.Line, node.Column);
            _stepMode = StepMode.None;
            _exceptionPause = true;
            _resumeLocation = node.Line > 0 ? (node.Line, _frames.Count - 1) : null;
            snapshot = CreateSnapshotLocked(node, environment, DebugPauseReason.Exception, exception);
            _threadGate.Reset();
            stateChange = SetStateLocked(DebugSessionState.Paused);
        }

        PublishStateChange(stateChange);
        Paused?.Invoke(this, new DebugPausedEventArgs(snapshot));
        WaitForResume();
    }

    public void OnFunctionEnter(string functionName, FishboneEnvironment environment)
    {
        lock (_sync)
        {
            _frames.Add(new Frame(functionName, environment));
        }
    }

    public void OnFunctionExit(string functionName)
    {
        lock (_sync)
        {
            if (_frames.Count > 1)
                _frames.RemoveAt(_frames.Count - 1);
        }
    }

    public void OnExecutionCompleted(FishboneEnvironment environment)
    {
        DebugStateChangedEventArgs? stateChange;
        lock (_sync)
        {
            _threadGate.Set();
            stateChange = SetStateLocked(DebugSessionState.Completed);
        }
        PublishStateChange(stateChange);
    }

    private void Resume(StepMode stepMode)
    {
        DebugStateChangedEventArgs? stateChange;
        lock (_sync)
        {
            _lastResumeWasSuccessful = false;

            if (_state != DebugSessionState.Paused)
                return;

            if (_exceptionPause && stepMode != StepMode.None)
                return;

            _stepMode = stepMode;
            _exceptionPause = false;
            _targetDepth = _frames.Count - 1;
            _lastResumeWasSuccessful = true;
            stateChange = SetStateLocked(DebugSessionState.Running);
            _threadGate.Set();
        }
        PublishStateChange(stateChange);
    }

    private bool ShouldPauseForStepLocked((int Line, int Depth) location)
    {
        if (_stepMode == StepMode.None || location == _resumeLocation)
            return false;

        return _stepMode switch
        {
            StepMode.Into => true,
            StepMode.Over => location.Depth <= _targetDepth,
            StepMode.Out => location.Depth < _targetDepth,
            _ => false
        };
    }

    private void WaitForResume()
    {
        _threadGate.Wait(_cancellationToken);
        lock (_sync) ThrowIfStoppedLocked();
        _cancellationToken.ThrowIfCancellationRequested();
    }

    private DebugPauseSnapshot CreateSnapshotLocked(
        AstNode node,
        FishboneEnvironment environment,
        DebugPauseReason reason,
        Exception? exception)
    {
        var visible = new Dictionary<string, object?>();
        for (var scope = environment; scope is not null; scope = scope.Parent)
            foreach (var pair in scope.LocalValues)
                visible.TryAdd(pair.Key, pair.Value);

        var variables = visible
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new DebugVariableSnapshot(pair.Key, pair.Value))
            .ToImmutableArray();
        var frames = _frames
            .AsEnumerable()
            .Reverse()
            .Select(frame => new DebugCallFrameSnapshot(
                frame.Name,
                frame.Location ?? new DebugSourceLocation(SourceId, node.Line, node.Column),
                frame.Environment.LocalValues
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new DebugVariableSnapshot(pair.Key, pair.Value))
                    .ToImmutableArray()))
            .ToImmutableArray();

        return new DebugPauseSnapshot(
            new DebugSourceLocation(SourceId, node.Line, node.Column),
            reason,
            variables,
            frames,
            exception is null ? null : new DebugExceptionSnapshot(exception.GetType().FullName ?? exception.GetType().Name, exception.Message));
    }

    private DebugStateChangedEventArgs? SetStateLocked(DebugSessionState state)
    {
        if (_state == state) return null;
        var previous = _state;
        _state = state;
        return new DebugStateChangedEventArgs(previous, state);
    }

    private void ThrowIfStoppedLocked()
    {
        if (_stopRequested)
            throw new OperationCanceledException("Debug session was stopped.");
    }

    private void PublishStateChange(DebugStateChangedEventArgs? args)
    {
        if (args is not null)
            StateChanged?.Invoke(this, args);
    }

    public void Dispose()
    {
        _cancellationRegistration.Dispose();
        _threadGate.Dispose();
    }
}