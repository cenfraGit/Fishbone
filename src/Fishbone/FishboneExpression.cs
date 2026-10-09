// --------------------------------------------------------------------------------
// FishboneExpression.cs
//
// evaluates a single expression against an environment a script already made, like a
// debugger's watch does while the script is paused.
// --------------------------------------------------------------------------------

using Fishbone.Interpreter;
using Fishbone.Parser;

namespace Fishbone;

public static class FishboneExpression
{
    /// <summary>
    /// The value of <paramref name="expression"/> in <paramref name="environment"/>, such as a
    /// paused script's current scope. It's real code: a call or an assignment runs, and changes what
    /// the script sees. It runs in a scope of its own below the environment, without a debugger.
    /// </summary>
    /// <param name="configuration">The configuration the script runs with, for its type converters
    /// and member access setting. Its built-ins and values are already in the environment.</param>
    /// <exception cref="FishboneParseException">The expression isn't valid. The message names the column.</exception>
    /// <exception cref="FishboneRuntimeException">Evaluating it failed.</exception>
    public static object? Evaluate(string expression, FishboneEnvironment environment,
        FishboneConfiguration? configuration = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(environment);
        configuration ??= new FishboneConfiguration();

        var node = ASTParser.ParseExpression(expression);
        var interpreter = new FishboneInterpreter(cancellationToken, null, configuration.TypeConverters, configuration.EnableMemberAccess);
        return interpreter.Evaluate(new FishboneEnvironment(environment), node);
    }
}
