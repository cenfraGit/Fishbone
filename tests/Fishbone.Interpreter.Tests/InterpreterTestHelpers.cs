using Fishbone.Core;
using Fishbone.Parser;

namespace Fishbone.Interpreter.Tests;

internal static class InterpreterTestHelpers
{
    public static FishboneEnvironment Run(string code)
    {
        var ast = ASTParser.Parse(code);
        var env = new FishboneEnvironment();
        var interpreter = new FishboneInterpreter();

        interpreter.Evaluate(env, ast);
        return env;
    }
}
