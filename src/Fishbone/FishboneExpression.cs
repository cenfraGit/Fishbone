// --------------------------------------------------------------------------------
// FishboneExpression.cs
//
// evaluates a single expression against an environment a script already made, like a
// debugger's watch does while the script is paused.
// --------------------------------------------------------------------------------

using Fishbone.Ast;
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
    /// <remarks>
    /// An <c>out</c> argument always lands in that scope, even when the script has a variable by
    /// the same name. A call that returns nothing, like a HALCON operator, gives its <c>out</c>
    /// values instead: the value itself for one, or a dictionary by name for several.
    /// </remarks>
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
        var scope = new FishboneEnvironment(environment);
        string[] outs = OutNames(node).Distinct().ToArray();
        foreach (string name in outs)
            scope.Declare(name, null!);

        var interpreter = new FishboneInterpreter(cancellationToken, null, configuration.TypeConverters, configuration.EnableMemberAccess);
        object? value = interpreter.Evaluate(scope, node);
        if (value is not null || outs.Length == 0)
            return value;
        return outs.Length == 1 ? scope.Values[outs[0]] : outs.ToDictionary(name => name, name => (object?)scope.Values[name]);
    }

    // the variables passed as out arguments, in the order they're written
    private static IEnumerable<string> OutNames(AstNode node) =>
        (node is CallNode call
            ? call.Arguments.Where(argument => argument.Modifier == ArgumentModifier.Out && argument.Value is IdentifierNode)
                .Select(argument => ((IdentifierNode)argument.Value).Name)
            : [])
        .Concat(FishboneAnalysis.Children(node).SelectMany(OutNames));
}
