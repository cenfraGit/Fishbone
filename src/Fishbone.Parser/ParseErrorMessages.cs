// --------------------------------------------------------------------------------
// ParseErrorMessages.cs
//
// rewrites antlr's own error text into something a script author can act on.
//
// antlr phrases errors in terms of its internals, and the worst of it is the
// "expecting" set, which dumps grammar token names at the user:
//
//   mismatched input '<EOF>' expecting {'{', '(', '[', INT, DOUBLE, STRING,
//   RAW_STRING, INTERP_STRING, '-', 'null', 'not', 'true', 'false', ID}
//
// nobody writing a script knows what INTERP_STRING is. so a set is kept only when it is
// short and every item is a concrete token: "expected ']' or ','" is the most useful thing
// the parser can say about an unclosed list, while a set naming grammar rules is dropped.
// listing only the literals from a mixed set would mislead, since the names it omits are
// usually the likely intent (an identifier where a parameter belongs, say).
//
// anything not recognised here falls through unchanged, so a grammar change that
// produces a new message shape degrades to antlr's wording rather than to nothing.
// the original is always kept on the diagnostic as RawMessage.
// --------------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Fishbone.Parser;

internal static partial class ParseErrorMessages
{
    // antlr writes the end of input as this in messages and as token text
    private const string EofText = "<EOF>";

    [GeneratedRegex(@"^token recognition error at: '(?<text>.*)'$", RegexOptions.Singleline)]
    private static partial Regex TokenRecognition();

    [GeneratedRegex(@"^missing (?<expected>.+?) at (?<found>.+)$", RegexOptions.Singleline)]
    private static partial Regex MissingToken();

    [GeneratedRegex(@"^extraneous input (?<found>.+?) expecting (?<expected>.+)$", RegexOptions.Singleline)]
    private static partial Regex ExtraneousInput();

    [GeneratedRegex(@"^mismatched input (?<found>.+?) expecting (?<expected>.+)$", RegexOptions.Singleline)]
    private static partial Regex MismatchedInput();

    [GeneratedRegex(@"^no viable alternative at input '(?<text>.*)'$", RegexOptions.Singleline)]
    private static partial Regex NoViableAlternative();

    /// <summary>
    /// Rewrites an ANTLR message, or returns null when the shape is not recognised and the caller
    /// should keep ANTLR's own wording. <paramref name="offendingText"/> is the token the parser
    /// choked on, when one is known; some ANTLR messages quote something far less useful.
    /// </summary>
    public static string? Rewrite(string message, string? offendingText = null, string? expectedSet = null)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        var recognition = TokenRecognition().Match(message);
        if (recognition.Success)
            return DescribeUnrecognizedText(recognition.Groups["text"].Value);

        var missing = MissingToken().Match(message);
        if (missing.Success)
        {
            var expected = missing.Groups["expected"].Value;
            var found = missing.Groups["found"].Value;
            return IsEof(found)
                ? $"Missing {expected} at end of file."
                : $"Missing {expected} before {found}.";
        }

        var extraneous = ExtraneousInput().Match(message);
        if (extraneous.Success)
            return Unexpected(extraneous.Groups["found"].Value, extraneous.Groups["expected"].Value);

        var mismatched = MismatchedInput().Match(message);
        if (mismatched.Success)
            return Unexpected(mismatched.Groups["found"].Value, mismatched.Groups["expected"].Value);

        var noViable = NoViableAlternative().Match(message);
        if (noViable.Success)
        {
            // antlr quotes everything it consumed since the rule started, as concatenated token
            // text with the whitespace gone. echoing that shows the author a mangled version of
            // their own line ("a=[1,2,3;"), so prefer the single token that actually failed
            var token = Condense(offendingText ?? string.Empty);
            if (token.Length == 0)
                return "This is not valid Fishbone syntax.";

            // this message shape carries no expecting set of its own, so the caller reads one off
            // the parser state. that is what turns "';' is not valid here" into the actionable
            // "expected ']'" for an unclosed list
            return Unexpected($"'{token}'", expectedSet ?? string.Empty);
        }

        return null;
    }

    // the lexer could not form a token. the common cause by far is a string that was
    // opened and never closed, which is worth naming outright: antlr reports it as an
    // unrecognised character run starting at the quote, which explains nothing
    private static string DescribeUnrecognizedText(string text)
    {
        var condensed = Condense(text);

        if (LooksLikeUnterminatedString(text))
            return "Unterminated string literal.";

        return condensed.Length switch
        {
            0 => "Unrecognized input.",
            1 => $"Unexpected character '{condensed}'.",
            _ => $"Unexpected input '{condensed}'."
        };
    }

    // a string token that never closed: the run starts with one of the three opening
    // forms and has no closing quote after it
    private static bool LooksLikeUnterminatedString(string text)
    {
        int quote = text.StartsWith("@\"", StringComparison.Ordinal) || text.StartsWith("$\"", StringComparison.Ordinal)
            ? 1
            : text.StartsWith('"') ? 0 : -1;

        return quote >= 0 && text.IndexOf('"', quote + 1) < 0;
    }

    private static string Unexpected(string found, string expected)
    {
        var subject = IsEof(found) ? "Unexpected end of file" : $"Unexpected {Condense(found)}";

        return DescribeExpectation(expected) is { } expectation
            ? $"{subject}, {expectation}."
            : $"{subject}.";
    }

    /// <summary>
    /// Turns ANTLR's "expecting" set into something worth showing, or null to omit it.
    /// </summary>
    private static string? DescribeExpectation(string expected)
    {
        var trimmed = expected.Trim();

        // a lone expectation is unbraced, and is the most useful thing the parser can say
        if (!trimmed.StartsWith('{'))
            return IsQuotedLiteral(trimmed) ? $"expected {trimmed}" : null;

        var items = trimmed.Trim('{', '}').Split(", ", StringSplitOptions.TrimEntries);

        // a set naming grammar rules rather than literals ('INTERP_STRING', 'ID') is an internal
        // dump. listing only its literals would also mislead, because the omitted names are
        // usually the likely intent, so the whole set is dropped unless every item is a literal
        if (items.Length == 0 || items.Length > 3 || !items.All(IsQuotedLiteral))
            return null;

        return "expected " + items.Length switch
        {
            1 => items[0],
            2 => $"{items[0]} or {items[1]}",
            _ => $"{string.Join(", ", items[..^1])}, or {items[^1]}"
        };
    }

    private static bool IsEof(string text) => text.Contains(EofText, StringComparison.Ordinal);

    // antlr quotes a concrete token ("';'") but names a grammar rule bare ("ID", "INTERP_STRING")
    private static bool IsQuotedLiteral(string item) =>
        item.Length >= 2 && item[0] == '\'' && item[^1] == '\'';

    // error text can run to the end of a line and carry the newline with it, which would
    // otherwise show up inside the message
    private static string Condense(string text)
    {
        var unescaped = UnescapeAntlrText(text).Trim();
        if (!unescaped.Contains('\n') && !unescaped.Contains('\r'))
            return unescaped;

        return unescaped.ReplaceLineEndings(" ").Trim();
    }

    /// <summary>
    /// Reverses the escaping ANTLR applies to text it quotes in a message. A newline is rendered
    /// as the two characters <c>\</c> and <c>n</c>, so a caller measuring that text to size a
    /// span would count one character too many for every escape, and a message repeating it
    /// would show the escape rather than the character.
    /// </summary>
    public static string UnescapeAntlrText(string text)
    {
        if (!text.Contains('\\'))
            return text;

        return text
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t");
    }
}