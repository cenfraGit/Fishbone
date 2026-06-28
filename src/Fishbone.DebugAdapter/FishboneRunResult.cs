using Fishbone.Core;

namespace Fishbone.DebugAdapter;

/// <summary>
/// The outcome of a debuggable run. <see cref="Environment"/> is the same kind of environment a
/// headless run returns, whether or not a debugger attached.
/// </summary>
public sealed class FishboneRunResult
{
    public FishboneRunResult(FishboneEnvironment? environment, Exception? error, bool debuggerAttached, bool wasCancelled)
    {
        Environment = environment;
        Error = error;
        DebuggerAttached = debuggerAttached;
        WasCancelled = wasCancelled;
    }

    /// <summary>The resulting environment, or null if the run failed before producing one.</summary>
    public FishboneEnvironment? Environment { get; }

    /// <summary>The error that ended the run, if any.</summary>
    public Exception? Error { get; }

    /// <summary>True if a debug client attached and the run executed under the debugger.</summary>
    public bool DebuggerAttached { get; }

    /// <summary>True if the run was cancelled.</summary>
    public bool WasCancelled { get; }
}