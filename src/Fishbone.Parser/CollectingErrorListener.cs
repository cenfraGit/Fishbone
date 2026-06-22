using Antlr4.Runtime;
using Antlr4.Runtime.Misc;

namespace Fishbone.Parser;

public readonly record struct ParseError(int Line, int Column, string Message, string? OffendingText);

public sealed class CollectingErrorListener : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<ParseError> Errors { get; } = [];

    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
        => Errors.Add(new ParseError(line, charPositionInLine + 1, msg, offendingSymbol?.Text));

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
        int line, int charPositionInLine, string msg, RecognitionException e)
        => Errors.Add(new ParseError(line, charPositionInLine + 1, msg, null));
}
