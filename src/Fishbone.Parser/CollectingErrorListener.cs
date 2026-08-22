// --------------------------------------------------------------------------------
// CollectingErrorListener.cs
//
// custom error listener for Fishbone lexer and parser.
//
// antlr's default lexer and parser each register a
// ConsoleErrorListener that outputs to stderr. our error listener
// should structure errors in a more useful format
//
// each error just appends a ParseError to the Errors instead of
// throwing, this is done so multiple errors can be accumulated and
// reported in the same parse.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace Fishbone.Parser;

public readonly record struct ParseError(int Line, int Column, string Message, string? OffendingText);

// implement both <IToken> and <int> error listener interfaces so it
// can be registered on both the lexer and parser
internal sealed class CollectingErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<ParseError> Errors { get; } = [];

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
        => Errors.Add(new ParseError(line, charPositionInLine + 1, msg, offendingSymbol?.Text));

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
        => Errors.Add(new ParseError(line, charPositionInLine + 1, msg, null));
}