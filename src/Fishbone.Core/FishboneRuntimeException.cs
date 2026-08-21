// --------------------------------------------------------------------------------
// FishboneRuntimeException.cs
//
// the exception used for every runtime error. holds line and column info.
//
// inner exception: if null, means the error was diagnosed by the
// language itself (undefined variable, invalid cast, etc). non-null
// means .NET call made by the script threw, and the inner exception
// is that original exception. (not syntax errors, those belong to
// ASTParser.
// --------------------------------------------------------------------------------

namespace Fishbone.Core;

public class FishboneRuntimeException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public FishboneRuntimeException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    public FishboneRuntimeException(string message, int line, int column, Exception? inner = null)
        : base(message, inner)
    {
        Line = line;
        Column = column;
    }
}