using Fishbone.Core;

namespace Fishbone.Debugging;

public sealed class NullFishboneDebugger : IFishboneDebugger
{
    public static NullFishboneDebugger Instance { get; } = new();

    private NullFishboneDebugger() { }

    public void OnExecutionStarted(AstNode root, FishboneEnvironment environment) { }
    public void OnBeforeExecute(AstNode node, FishboneEnvironment environment) { }
    public void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment) { }
    public void OnFunctionEnter(string functionName, FishboneEnvironment environment) { }
    public void OnFunctionExit(string functionName) { }
    public void OnExecutionCompleted(FishboneEnvironment environment) { }
}