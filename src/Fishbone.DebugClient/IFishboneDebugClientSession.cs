namespace Fishbone.DebugClient;

public interface IFishboneDebugClientSession : IAsyncDisposable
{
    event EventHandler<FishboneDebugEvent>? EventReceived;
    FishboneDebugSessionState State { get; }
    string ScriptPath { get; }
    Task StartAsync(IReadOnlyList<int> breakpoints, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FishboneBreakpointResult>> SetBreakpointsAsync(IReadOnlyList<int> lines, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FishboneDebugVariable>> GetVariablesAsync(FishboneVariableHandle handle, CancellationToken cancellationToken = default);
    Task ContinueAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StepIntoAsync(CancellationToken cancellationToken = default);
    Task StepOverAsync(CancellationToken cancellationToken = default);
    Task StepOutAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IFishboneDebugClientSessionFactory
{
    IFishboneDebugClientSession Create(string scriptPath);
}

public sealed class FishboneDebugClientSessionFactory(IFishboneDapHostLocator hostLocator) : IFishboneDebugClientSessionFactory
{
    public IFishboneDebugClientSession Create(string scriptPath) => new FishboneDebugClientSession(scriptPath, hostLocator);
}