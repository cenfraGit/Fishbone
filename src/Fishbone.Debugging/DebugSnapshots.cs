using System.Collections.Immutable;

namespace Fishbone.Debugging;

public enum DebugSessionState
{
    Running,
    Paused,
    Stopping,
    Completed
}

public enum DebugPauseReason
{
    Breakpoint,
    ManualPause,
    Step,
    Exception
}

public sealed record DebugSourceLocation(string SourceId, int Line, int Column);
public sealed record DebugVariableSnapshot(string Name, object? Value);
public sealed record DebugExceptionSnapshot(string Type, string Message);

public sealed record DebugCallFrameSnapshot(
    string FunctionName,
    DebugSourceLocation Location,
    ImmutableArray<DebugVariableSnapshot> Variables);

public sealed record DebugPauseSnapshot(
    DebugSourceLocation Location,
    DebugPauseReason Reason,
    ImmutableArray<DebugVariableSnapshot> VisibleVariables,
    ImmutableArray<DebugCallFrameSnapshot> CallStack,
    DebugExceptionSnapshot? Exception);

public sealed class DebugPausedEventArgs(DebugPauseSnapshot snapshot) : EventArgs
{
    public DebugPauseSnapshot Snapshot { get; } = snapshot;
}

public sealed class DebugStateChangedEventArgs(DebugSessionState previousState, DebugSessionState state) : EventArgs
{
    public DebugSessionState PreviousState { get; } = previousState;
    public DebugSessionState State { get; } = state;
}