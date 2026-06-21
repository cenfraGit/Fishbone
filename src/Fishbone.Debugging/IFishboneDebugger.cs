using Fishbone.Core;

namespace Fishbone.Debugging;

public interface IFishboneDebugger
{
    void OnExecutionStarted(AstNode root, FishboneEnvironment environment);
    void OnBeforeExecute(AstNode node, FishboneEnvironment environment);
    void OnRuntimeException(Exception exception, AstNode node, FishboneEnvironment environment);
    void OnFunctionEnter(string functionName, FishboneEnvironment environment);
    void OnFunctionExit(string functionName);
    void OnExecutionCompleted(FishboneEnvironment environment);
}