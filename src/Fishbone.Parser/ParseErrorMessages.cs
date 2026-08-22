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
// nobody writing a script knows what INTERP_STRING is. so a large expecting set is
// dropped entirely, while a set of one is kept, because "expecting ';'" is the most
// useful thing the parser can say.
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
    /// Rewrites an ANTLR message, or returns null when the shape is not recognised and the
    /// caller should keep ANTLR's own wording.
    /// </summary>
    public static string? Rewrite(string message)
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
            var text = Condense(noViable.Groups["text"].Value);
            return text.Length == 0
                ? "This is not valid Fishbone syntax."
                : $"'{text}' is not valid Fishbone syntax.";
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

    // an "expecting" set of one is worth reporting; a longer one is a token-name dump
    private static string Unexpected(string found, string expected)
    {
        var subject = IsEof(found) ? "Unexpected end of file" : $"Unexpected {Condense(found)}";

        return IsSingleExpectation(expected)
            ? $"{subject}, expected {expected.Trim()}."
            : $"{subject}.";
    }

    // antlr renders a set as {a, b, c} and a lone expectation bare. a braced set holding
    // exactly one item counts as single too
    private static bool IsSingleExpectation(string expected)
    {
        var trimmed = expected.Trim();
        if (!trimmed.StartsWith('{'))
            return true;

        return !trimmed.Contains(',');
    }

    private static bool IsEof(string text) => text.Contains(EofText, StringComparison.Ordinal);

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