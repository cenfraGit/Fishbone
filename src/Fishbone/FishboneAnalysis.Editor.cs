// --------------------------------------------------------------------------------
// FishboneAnalysis.Editor.cs
//
// what an editor asks while the user types: what to suggest at the caret, and
// which call the caret is in. it works on the current text, which usually doesn't
// parse, so it reads tokens and takes scopes from the last analysis that did. a
// debugger's watch asks the same, as if the watch were typed into the script.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Fishbone.Parser;

namespace Fishbone;

public enum FishboneSuggestionKind
{
    Keyword,
    /// <summary>A script variable or parameter.</summary>
    Variable,
    /// <summary>A script function, or a function from the configuration.</summary>
    Function,
    /// <summary>A registered type.</summary>
    Type,
    /// <summary>A value from <see cref="FishboneConfiguration.Values"/>.</summary>
    Value,
    Constant,
    Property,
    Field,
    Method
}

/// <summary>One entry for a completion list.</summary>
public sealed record FishboneSuggestion(string Text, FishboneSuggestionKind Kind);

/// <summary>
/// What to suggest at a caret. <see cref="Start"/> is where the word being typed begins, so an
/// editor replaces the text from there to the caret.
/// </summary>
public sealed record FishboneCompletions(int Start, IReadOnlyList<FishboneSuggestion> Items);

/// <summary>The call the caret is inside.</summary>
/// <param name="OpenParen">Where its <c>(</c> is in the text.</param>
/// <param name="Signatures">Its overloads. A script function's parameters have the type <see cref="object"/>.</param>
/// <param name="Argument">Which argument the caret is on, counting from 0.</param>
public sealed record FishboneCallInfo(string Name, int OpenParen, IReadOnlyList<FishboneSignature> Signatures, int Argument);

public sealed partial class FishboneAnalysis
{
    private static readonly string[] Keywords =
    [
        "let", "func", "if", "else", "while", "foreach", "for", "in", "as", "return", "break", "continue",
        "try", "catch", "finally", "throw", "true", "false", "null", "and", "or", "xor", "not", "out", "ref"
    ];

    /// <summary>
    /// The completion list at <paramref name="caret"/>, a character index into <paramref name="text"/>.
    /// After a dot it's the members of the expression before it, when its type is known. Otherwise
    /// it's keywords, script names in scope and the configuration's names. Everything starts with the
    /// word being typed. Null inside a comment or a string, or when nothing applies.
    /// </summary>
    /// <remarks>
    /// The text can differ from the script this analysis was made from. Usually it's the same script
    /// a few keystrokes later, which doesn't parse yet.
    /// </remarks>
    public FishboneCompletions? CompletionsAt(string text, int caret)
    {
        if (CodeTokens(text, caret) is not { } tokens)
            return null;

        // the word being typed is the identifier that ends at the caret
        int start = caret, last = tokens.Count - 1;
        if (last >= 0 && tokens[last].Type == FishboneLexer.ID && tokens[last].StopIndex + 1 == caret)
            start = tokens[last--].StartIndex;
        string prefix = text[start..caret];

        IEnumerable<FishboneSuggestion> items;
        if (last >= 0 && tokens[last].Text == ".")
        {
            if (TypeBefore(text, tokens, last) is not { } type)
                return null;
            items = MemberSuggestions(type.Type, type.IsStatic);
        }
        else
        {
            var (line, column) = LineAndColumn(text, start);
            items = Keywords.Select(keyword => new FishboneSuggestion(keyword, FishboneSuggestionKind.Keyword))
                .Concat(VisibleAt(line, column).Select(variable => new FishboneSuggestion(variable.Name,
                    variable.Parameters is null ? FishboneSuggestionKind.Variable : FishboneSuggestionKind.Function)))
                .Concat(_description.Symbols.Select(symbol => new FishboneSuggestion(symbol.Name, symbol.Kind switch
                {
                    FishboneSymbolKind.Value => FishboneSuggestionKind.Value,
                    FishboneSymbolKind.Function => FishboneSuggestionKind.Function,
                    FishboneSymbolKind.Type => FishboneSuggestionKind.Type,
                    _ => FishboneSuggestionKind.Constant
                })));
        }

        return Completions(start, prefix, items);
    }

    /// <summary>
    /// The completion list for a debugger's watch: <paramref name="expression"/> as if it were typed
    /// at the start of <paramref name="line"/> in <paramref name="source"/>, the script this analysis
    /// was made from, so the names in scope there are offered. A line past the end means after the
    /// script. <paramref name="caret"/>, and the result's <see cref="FishboneCompletions.Start"/>, are
    /// indexes into the expression.
    /// </summary>
    /// <param name="evaluate">
    /// The value of an expression, such as a paused script's. When the type before a dot isn't known
    /// and the text before it is a name or a chain of names, like <c>result.Items</c>, its value's
    /// members are listed. Nothing with a call or an index is evaluated, since that would run code.
    /// </param>
    public FishboneCompletions? WatchCompletionsAt(string source, int line, string expression, int caret,
        Func<string, object?>? evaluate = null)
    {
        string before = LinesBefore(source, line);
        if (CompletionsAt(before + expression, before.Length + caret) is { } completions)
            return completions with { Start = completions.Start - before.Length };
        return evaluate is null ? null : ValueMembersAt(expression, caret, evaluate);
    }

    /// <summary>
    /// The call the caret is inside in a debugger's watch, typed as <see cref="WatchCompletionsAt"/>
    /// says. The result's <see cref="FishboneCallInfo.OpenParen"/> is an index into the expression.
    /// </summary>
    public FishboneCallInfo? WatchCallAt(string source, int line, string expression, int caret)
    {
        string before = LinesBefore(source, line);
        return CallAt(before + expression, before.Length + caret) is { } call && call.OpenParen >= before.Length
            ? call with { OpenParen = call.OpenParen - before.Length }
            : null;
    }

    // the script up to the start of a line, or all of it and a line break past its end
    private static string LinesBefore(string source, int line)
    {
        int offset = 0;
        for (int i = 1; i < line && offset >= 0; i++)
            offset = source.IndexOf('\n', offset) is var next and >= 0 ? next + 1 : -1;
        return offset >= 0 ? source[..offset] : source + "\n";
    }

    // the members of the value before the dot at the caret, when it's a chain of names
    private FishboneCompletions? ValueMembersAt(string expression, int caret, Func<string, object?> evaluate)
    {
        if (CodeTokens(expression, caret) is not { } tokens)
            return null;
        int start = caret, last = tokens.Count - 1;
        if (last >= 0 && tokens[last].Type == FishboneLexer.ID && tokens[last].StopIndex + 1 == caret)
            start = tokens[last--].StartIndex;
        if (last < 1 || tokens[last].Text != ".")
            return null;

        // back over name, dot, name. it has to start the expression or follow an operator, not a ')'
        int first = last - 1;
        while (first >= 2 && tokens[first].Type == FishboneLexer.ID && tokens[first - 1].Text == "." && tokens[first - 2].Type == FishboneLexer.ID)
            first -= 2;
        if (tokens[first].Type != FishboneLexer.ID || (first > 0 && tokens[first - 1].Text is "." or ")" or "]"))
            return null;

        object? value;
        try
        {
            value = evaluate(expression[tokens[first].StartIndex..tokens[last].StartIndex]);
        }
        catch
        {
            return null;
        }
        return value is null ? null : Completions(start, expression[start..caret], MemberSuggestions(value.GetType(), false));
    }

    private IEnumerable<FishboneSuggestion> MemberSuggestions(Type type, bool isStatic) =>
        _description.Members(type, isStatic).Select(member => new FishboneSuggestion(member.Name, member.Kind switch
        {
            FishboneMemberKind.Property => FishboneSuggestionKind.Property,
            FishboneMemberKind.Field => FishboneSuggestionKind.Field,
            _ => FishboneSuggestionKind.Method
        }));

    // the first entry for a name wins, so a script variable hides a configuration name
    private static FishboneCompletions? Completions(int start, string prefix, IEnumerable<FishboneSuggestion> items)
    {
        var list = items
            .Where(item => item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(item => item.Text)
            .ToList();
        return list.Count == 0 ? null : new FishboneCompletions(start, list);
    }

    /// <summary>
    /// The call <paramref name="caret"/> is inside, with its overloads and the argument the caret is
    /// on. Null outside a call, inside a comment or a string, or when the callee isn't known.
    /// </summary>
    public FishboneCallInfo? CallAt(string text, int caret)
    {
        if (CodeTokens(text, caret) is not { } tokens)
            return null;

        // back to the '(' that's still open. an open list, dictionary or block, or a ';', means
        // the caret isn't on an argument
        int depth = 0, open = -1;
        for (int i = tokens.Count - 1; i >= 0 && open < 0; i--)
        {
            string token = tokens[i].Text;
            if (token is ")" or "]" or "}")
                depth++;
            else if (token is "(" or "[" or "{")
            {
                if (depth > 0)
                    depth--;
                else if (token == "(")
                    open = i;
                else
                    return null;
            }
            else if (depth == 0 && token == ";")
                return null;
        }
        if (open < 1 || tokens[open - 1].Type != FishboneLexer.ID)
            return null;

        int argument = 0;
        foreach (var token in tokens.Skip(open + 1))
        {
            if (token.Text is "(" or "[" or "{")
                depth++;
            else if (token.Text is ")" or "]" or "}")
                depth--;
            else if (token.Text == "," && depth == 0)
                argument++;
        }

        string name = tokens[open - 1].Text;
        IReadOnlyList<FishboneSignature>? signatures;
        if (open >= 2 && tokens[open - 2].Text == ".")
        {
            // a method: its overloads on the type of what's before the dot
            signatures = TypeBefore(text, tokens, open - 2) is { } type
                ? _description.Members(type.Type, type.IsStatic)
                    .FirstOrDefault(member => member.Name == name && member.Kind == FishboneMemberKind.Method)?.Signatures
                : null;
        }
        else
        {
            var (line, column) = LineAndColumn(text, tokens[open - 1].StartIndex);
            // a script name hides the configuration's
            if (VisibleAt(line, column).FirstOrDefault(variable => variable.Name == name) is { } script)
                signatures = script.Parameters is null ? null :
                [
                    new FishboneSignature(script.Parameters
                        .Select(parameter => new FishboneSignatureParameter(parameter, typeof(object), ParameterDirection.In, false, null))
                        .ToArray(), typeof(object))
                ];
            else
                signatures = _symbols.TryGetValue(name, out var symbol) && !_assigned.Contains(name) ? symbol.Signatures : null;
        }

        return signatures is { Count: > 0 } ? new FishboneCallInfo(name, tokens[open].StartIndex, signatures, argument) : null;
    }

    // the code tokens before the caret, or null when the caret is in a comment or a string.
    // the lexer skips comments, so text between the last token and the caret that isn't
    // whitespace is a line comment. an unfinished string is a lexer error on the caret's line
    private static List<IToken>? CodeTokens(string text, int caret)
    {
        // AntlrInputStream counts utf-16 chars, so token indexes match the caret's
        var lexer = new FishboneLexer(new AntlrInputStream(text[..caret]));
        var errors = new CollectingErrorListener();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(errors);
        var tokens = new List<IToken>();
        for (var token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
            tokens.Add(token);

        int end = tokens.Count == 0 ? 0 : tokens[^1].StopIndex + 1;
        int caretLine = text.AsSpan(0, caret).Count('\n') + 1;
        if (end > caret || !string.IsNullOrWhiteSpace(text[end..caret]) || errors.Errors.Any(error => error.Line == caretLine))
            return null;
        // an unclosed block comment lexes as '/' then '*', which can't be next to each other in code
        for (int i = 1; i < tokens.Count; i++)
            if (tokens[i - 1].Text == "/" && tokens[i].Text == "*" && tokens[i - 1].StopIndex + 1 == tokens[i].StartIndex)
                return null;
        return tokens;
    }

    // the type of the expression that ends just before tokens[end]: names, members, calls,
    // indexes and string literals, like a.b(1)[2] or "text". a number before a dot is a decimal
    // being typed, not member access
    private FishboneExpressionType? TypeBefore(string text, List<IToken> tokens, int end)
    {
        int i = end - 1;
        while (i >= 0)
        {
            string token = tokens[i].Text;
            if (token is ")" or "]")
            {
                // skip the whole group, then whatever it follows: a callee, an indexed value, or nothing
                int depth = 0;
                for (; i >= 0; i--)
                {
                    if (tokens[i].Text is ")" or "]")
                        depth++;
                    else if (tokens[i].Text is "(" or "[" && --depth == 0)
                        break;
                }
                i--;
                continue;
            }
            if (tokens[i].Type is FishboneLexer.STRING or FishboneLexer.RAW_STRING or FishboneLexer.INTERP_STRING)
            {
                i--;
                break;
            }
            if (tokens[i].Type != FishboneLexer.ID)
                break;
            i--;
            if (i < 0 || tokens[i].Text != ".")
                break;
            i--;
        }

        int start = i + 1;
        if (start >= end || tokens[start].Type is FishboneLexer.INT or FishboneLexer.DOUBLE)
            return null;
        var (line, column) = LineAndColumn(text, tokens[start].StartIndex);
        return TypeOf(text[tokens[start].StartIndex..tokens[end].StartIndex], line, column);
    }

    private static (int Line, int Column) LineAndColumn(string text, int index)
    {
        int lineStart = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        return (text.AsSpan(0, index).Count('\n') + 1, index - lineStart + 1);
    }
}
