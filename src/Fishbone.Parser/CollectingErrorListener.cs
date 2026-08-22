// --------------------------------------------------------------------------------
// CollectingErrorListener.cs
//
// custom error listener for Fishbone lexer and parser.
//
// antlr's default lexer and parser each register a
// ConsoleErrorListener that outputs to stderr. our error listener
// should structure errors in a more useful format
//
// each error just appends a diagnostic to Diagnostics instead of
// throwing, this is done so multiple errors can be accumulated and
// reported in the same parse.
//
// two things happen on the way in. antlr's wording is rewritten by
// ParseErrorMessages so the user does not see grammar token names, and the offending
// token's extent is recorded, so a client can underline it rather than only place a
// caret. the stage (Lex or Parse) is recorded too, since the two arrive through
// different overloads here and used to be indistinguishable once collected.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Fishbone.Core;
using System.Text.RegularExpressions;

namespace Fishbone.Parser;

/// <summary>
/// A single syntax error. Retained as the shape <see cref="FishboneParseException.Errors"/>
/// exposes; <see cref="FishboneDiagnostic"/> is the richer form clients should prefer.
/// </summary>
public readonly record struct ParseError(int Line, int Column, string Message, string? OffendingText)
{
    private readonly SourceSpan _span;

    /// <summary>
    /// The extent of the error. Falls back to a zero-width span at
    /// <see cref="Line"/>/<see cref="Column"/> when constructed without one.
    /// </summary>
    public SourceSpan Span
    {
        get => _span.IsKnown ? _span : new SourceSpan(Line, Column);
        init => _span = value;
    }
}

// implement both <IToken> and <int> error listener interfaces so it
// can be registered on both the lexer and parser
internal sealed partial class CollectingErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<FishboneDiagnostic> Diagnostics { get; } = [];

    // the parser overload: an offending token is available, so its span is exact
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        var span = offendingSymbol is null
            ? new SourceSpan(line, charPositionInLine + 1)
            : offendingSymbol.Span();

        Add(DiagnosticStage.Parse, msg, span, offendingSymbol?.Text);
    }

    // the lexer overload: there is no token, because failing to build one is the error.
    // the characters it choked on are quoted inside the message, so the span is recovered
    // from there rather than left zero-width
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
    {
        // antlr escapes control characters in the text it quotes, so it has to be unescaped
        // before its length can stand in for the width of the span
        var offendingText = OffendingTextOf(msg) is { } quoted
            ? ParseErrorMessages.UnescapeAntlrText(quoted).TrimEnd()
            : null;
        int width = offendingText?.Length ?? 0;

        var span = new SourceSpan(line, charPositionInLine + 1, line, charPositionInLine + 1 + width);
        Add(DiagnosticStage.Lex, msg, span, offendingText);
    }

    private void Add(DiagnosticStage stage, string message, SourceSpan span, string? offendingText)
    {
        var rewritten = ParseErrorMessages.Rewrite(message);

        Diagnostics.Add(new FishboneDiagnostic(stage, DiagnosticSeverity.Error, rewritten ?? message, span)
        {
            OffendingText = offendingText,
            // only worth keeping when it differs, so a client showing it is not showing a duplicate
            RawMessage = rewritten is null ? null : message
        });
    }

    [GeneratedRegex(@"^token recognition error at: '(?<text>.*)'$", RegexOptions.Singleline)]
    private static partial Regex LexerOffendingText();

    private static string? OffendingTextOf(string message)
    {
        var match = LexerOffendingText().Match(message);
        return match.Success ? match.Groups["text"].Value : null;
    }
}