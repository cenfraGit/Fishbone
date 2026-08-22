// --------------------------------------------------------------------------------
// ParseSpans.cs
//
// turns antlr positions into SourceSpan.
//
// antlr counts columns from 0 while fishbone counts from 1, so every conversion
// adds one. it also reports only the start line of a token, so a token whose text
// contains newlines (a raw or interpolated string) needs its end worked out from
// the text itself rather than from the token's own line number.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Fishbone.Core;

namespace Fishbone.Parser;

internal static class ParseSpans
{
    // span covering a whole parse-tree context, from its first token to its last.
    // Stop is null when the context was never completed (a syntax error mid-rule),
    // in which case the start token is all we have
    public static SourceSpan Span(this ParserRuleContext context) =>
        context.Stop is null
            ? Span(context.Start)
            : Between(context.Start, context.Stop);

    // span covering a single token
    public static SourceSpan Span(this IToken token)
    {
        var (endLine, endColumn) = EndOf(token);
        return new SourceSpan(token.Line, token.Column + 1, endLine, endColumn);
    }

    // span from the start of one token to the end of another
    public static SourceSpan Between(IToken start, IToken stop)
    {
        var (endLine, endColumn) = EndOf(stop);
        return new SourceSpan(start.Line, start.Column + 1, endLine, endColumn);
    }

    // end position of a token, exclusive. EOF has no width, and its Text is the
    // literal "<EOF>" which would otherwise report five phantom characters
    private static (int Line, int Column) EndOf(IToken token)
    {
        if (token.Type == TokenConstants.EOF)
            return (token.Line, token.Column + 1);

        var text = token.Text ?? string.Empty;
        int lastNewline = text.LastIndexOf('\n');

        // single-line token: start column plus its length
        if (lastNewline < 0)
            return (token.Line, token.Column + 1 + text.Length);

        // multi-line token: the end lands on a later line, at the character after
        // whatever follows the final newline
        int newlines = 0;
        foreach (char character in text)
            if (character == '\n') newlines++;

        return (token.Line + newlines, text.Length - lastNewline);
    }
}