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

public enum FishboneDebugSessionOwnership { Launched, Attached }

public sealed record FishboneDebugSource(
    string Name,
    string? Identity,
    long Reference,
    string Content,
    string? MimeType);

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
    FishboneDebugException? Exception)
{
    /// <summary>
    /// <see cref="Reason"/> value for the final pause emitted when a stepping session runs off the
    /// end of the script. It carries the final variable state but is not an interactive stop, so a
    /// UI should show the variables, drop the current-line highlight, and let the session finish.
    /// </summary>
    public const string ProgramExitReason = "program-exit";
}

public abstract record FishboneDebugEvent;
public sealed record FishboneDebugStateChanged(FishboneDebugSessionState State) : FishboneDebugEvent;
public sealed record FishboneDebugPaused(FishbonePauseSnapshot Snapshot) : FishboneDebugEvent;
public sealed record FishboneDebugContinued : FishboneDebugEvent;
public sealed record FishboneDebugOutput(string Text, FishboneDebugOutputCategory Category) : FishboneDebugEvent;
public sealed record FishboneDebugTerminated(int? ExitCode) : FishboneDebugEvent;
public sealed record FishboneDebugFailed(Exception Exception) : FishboneDebugEvent;