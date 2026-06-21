using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Interpreter;
using Fishbone.Parser;

namespace Fishbone.Engine;

public static class FishboneEngine
{
    public static FishboneEnvironment Run(string sourceCode,
                                          FishboneConfiguration configuration,
                                          CancellationToken cancellationToken = default,
                                          IFishboneDebugger? debugger = null)
    {
        var ast = ASTParser.Parse(sourceCode);
        var envRoot = new FishboneEnvironment();

        foreach (var kvp in configuration.BuiltIns)
            envRoot.AddBuiltIn(kvp.Key, kvp.Value);

        var activeDebugger = debugger ?? NullFishboneDebugger.Instance;
        var interpreter = new FishboneInterpreter(cancellationToken, activeDebugger);
        activeDebugger.OnExecutionStarted(ast, envRoot);
        try
        {
            interpreter.Evaluate(envRoot, ast);
        }
        finally
        {
            activeDebugger.OnExecutionCompleted(envRoot);
        }

        return envRoot;
    }
}