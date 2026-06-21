using System.Collections.Immutable;

namespace Fishbone.DebugClient;

public enum FishboneDebugSessionState
{
    Starting,
    Running,
    Paused,
    Stopping,
    Completed,
    Faulted
}

public enum FishboneDebugOutputCategory { Stdout, Stderr, Console }

public sealed record FishboneBreakpointResult(int Line, bool Verified, string? Message);

public sealed record FishboneVariableHandle(int Generation, long Reference);

public sealed record FishboneDebugVariable(
    string Name,
    string Value,
    string? Type,
    FishboneVariableHandle? ChildrenHandle,
    long? NamedVariables,
    long? IndexedVariables);

public sealed record FishboneDebugScope(
    string Name,
    FishboneVariableHandle VariablesHandle,
    ImmutableArray<FishboneDebugVariable> Variables);

public sealed record FishboneDebugFrame(
    long Id,
    string Name,
    string? SourcePath,
    int Line,
    int Column,
    ImmutableArray<FishboneDebugScope> Scopes);

public sealed record FishboneDebugException(string Id, string? Description);

public sealed record FishbonePauseSnapshot(
    int Generation,
    string Reason,
    string? Description,
    ImmutableArray<FishboneDebugFrame> Frames,
    FishboneDebugException? Exception);

public abstract record FishboneDebugEvent;
public sealed record FishboneDebugStateChanged(FishboneDebugSessionState State) : FishboneDebugEvent;
public sealed record FishboneDebugPaused(FishbonePauseSnapshot Snapshot) : FishboneDebugEvent;
public sealed record FishboneDebugContinued : FishboneDebugEvent;
public sealed record FishboneDebugOutput(string Text, FishboneDebugOutputCategory Category) : FishboneDebugEvent;
public sealed record FishboneDebugTerminated(int? ExitCode) : FishboneDebugEvent;
public sealed record FishboneDebugFailed(Exception Exception) : FishboneDebugEvent;