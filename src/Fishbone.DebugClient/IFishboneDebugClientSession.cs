namespace Fishbone.DebugClient;

public interface IFishboneDebugClientSession : IAsyncDisposable
{
    event EventHandler<FishboneDebugEvent>? EventReceived;
    FishboneDebugSessionState State { get; }
    FishboneDebugSessionOwnership Ownership { get; }
    FishboneDebugSource? Source { get; }
    Task<FishboneDebugSource> ConnectAsync(bool stopOnEntry = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FishboneBreakpointResult>> ConfigureAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default);
    Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default);
    /// <summary>The image behind <see cref="FishboneDebugVariable.ImageHandle"/>, and the shapes to draw over it.</summary>
    Task<FishboneDebugImage> GetImageAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default);

    /// <summary>
    /// A watch: <paramref name="expression"/> evaluated in the paused script's top frame, and shown
    /// like a variable named after it. Throws with the reason when it can't be evaluated.
    /// </summary>
    Task<FishboneDebugVariable> EvaluateAsync(string expression, CancellationToken cancellationToken = default);

    /// <summary>What to suggest for a watch being typed, with <paramref name="caret"/> an index into it. Null when nothing applies.</summary>
    Task<FishboneDebugCompletions?> GetCompletionsAsync(string expression, int caret, CancellationToken cancellationToken = default);
    Task ContinueAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StepIntoAsync(CancellationToken cancellationToken = default);
    Task StepOverAsync(CancellationToken cancellationToken = default);
    Task StepOutAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IFishboneDebugClientSessionFactory
{
    IFishboneDebugClientSession CreateLaunched(string scriptPath);
    IFishboneDebugClientSession CreateAttached(string host, int port);
}

public sealed class FishboneDebugClientSessionFactory(IFishboneDapHostLocator hostLocator) : IFishboneDebugClientSessionFactory
{
    public IFishboneDebugClientSession CreateLaunched(string scriptPath) => new FishboneDebugClientSession(scriptPath, hostLocator);
    public IFishboneDebugClientSession CreateAttached(string host, int port) => FishboneDebugClientSession.Attach(host, port);
}