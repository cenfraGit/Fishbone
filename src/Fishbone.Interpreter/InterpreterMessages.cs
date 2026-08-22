// --------------------------------------------------------------------------------
// InterpreterMessages.cs
//
// wording for runtime errors that more than one call path can raise.
//
// argument binding happens in three places (a fishbone function, a manual callable,
// and a reflected method) and each used to phrase the same failure itself. the
// wording drifted: one path said "Expected 2 args but got 3." while another said
// "Expected 2 argument(s) but got 3." for the identical mistake. six messages had
// diverged this way. they live here now so a single edit changes every path.
//
// only shared messages belong here. a message raised from exactly one place reads
// better next to the check that produces it.
// --------------------------------------------------------------------------------

using Fishbone.Core;

namespace Fishbone.Interpreter;

internal static class InterpreterMessages
{
    public static string ArgumentCountMismatch(int expected, int actual) =>
        $"Expected {expected} argument{(expected == 1 ? "" : "s")} but got {actual}.";

    public static string ModifierRequired(string? parameterName, ArgumentModifier required) =>
        $"{Describe(parameterName)} is {Article(required)} {Keyword(required)} parameter; " +
        $"pass the argument with '{Keyword(required)}'.";

    public static string ModifierTargetMustBeVariable(string? parameterName, ArgumentModifier modifier) =>
        $"{Capitalize(Keyword(modifier))} argument {Quote(parameterName)} must be a variable.";

    public static string ModifierNotAllowed(string? parameterName, ArgumentModifier supplied) =>
        $"{Describe(parameterName)} is passed by value; remove '{Keyword(supplied)}'.";

    private static string Keyword(ArgumentModifier modifier) => modifier switch
    {
        ArgumentModifier.Out => "out",
        ArgumentModifier.Ref => "ref",
        _ => "value"
    };

    // "an out parameter" but "a ref parameter"
    private static string Article(ArgumentModifier modifier) =>
        modifier == ArgumentModifier.Out ? "an" : "a";

    // a reflected ParameterInfo can have no name, so neither helper may assume one
    private static string Describe(string? parameterName) =>
        parameterName is null ? "That parameter" : $"Parameter '{parameterName}'";

    private static string Quote(string? parameterName) =>
        parameterName is null ? "at that position" : $"'{parameterName}'";

    private static string Capitalize(string word) =>
        char.ToUpperInvariant(word[0]) + word[1..];
}