using Fishbone.Core;
using Fishbone.Interpreter;
using Fishbone.Parser;

namespace Fishbone.Engine;

public static class FishboneEngine
{
    public static FishboneEnvironment Run(string sourceCode, FishboneConfiguration configuration)
    {
        var ast = ASTParser.Parse(sourceCode);
        var envRoot = new FishboneEnvironment();

        // seed root env with config built-ins
        foreach (var kvp in configuration.BuiltIns)
            envRoot.AddBuiltIn(kvp.Key, kvp.Value);

        // eval program
        var interpreter = new FishboneInterpreter();
        interpreter.Evaluate(envRoot, ast);

        return envRoot;
    }
}