namespace Fishbone.Core;

/// <summary>
/// The exception type for every error raised while a script runs. <see cref="Line"/> and
/// <see cref="Column"/> locate the failing statement or expression in the script source
/// (zero means the location has not been attached yet; the interpreter attaches it as the
/// error surfaces).
///
/// <see cref="Exception.InnerException"/> distinguishes the two kinds of failure: null
/// means the error was diagnosed by the language itself (an undefined variable, indexing
/// null, an impossible conversion, ...); non-null means a .NET call made by the script
/// threw, and the inner exception is that original exception.
/// </summary>
public class FishboneRuntimeException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public FishboneRuntimeException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public FishboneRuntimeException(string message, int line, int column, Exception? inner = null)
        : base(message, inner)
    {
        Line = line;
        Column = column;
    }
}