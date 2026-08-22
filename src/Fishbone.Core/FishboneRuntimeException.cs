// --------------------------------------------------------------------------------
// FishboneRuntimeException.cs
//
// the exception used for every runtime error. holds the source span of the failing
// node (see FishboneInterpreter.Evaluate, which stamps it on the way out).
//
// inner exception: if null, means the error was diagnosed by the
// language itself (undefined variable, invalid cast, etc). non-null
// means .NET call made by the script threw, and the inner exception
// is that original exception. (not syntax errors, those belong to
// ASTParser.
//
// implements IFishboneDiagnosticSource so a client can go through
// FishboneDiagnostics.From instead of reading Line/Column off this type directly.
// --------------------------------------------------------------------------------

namespace Fishbone.Core;

public class FishboneRuntimeException : Exception, IFishboneDiagnosticSource
{
    private FishboneDiagnostic[]? _diagnostics;

    /// <summary>Where the failure happened, or <see cref="SourceSpan.None"/> if unknown.</summary>
    public SourceSpan Span { get; }

    public int Line => Span.Line;
    public int Column => Span.Column;

    public FishboneRuntimeException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    public FishboneRuntimeException(string message, int line, int column, Exception? inner = null)
        : this(message, new SourceSpan(line, column), inner)
    {
    }

    public FishboneRuntimeException(string message, SourceSpan span, Exception? inner = null)
        : base(message, inner)
    {
        Span = span;
    }

    public IReadOnlyList<FishboneDiagnostic> Diagnostics =>
        _diagnostics ??= [FishboneDiagnostics.Runtime(Message, Span)];
}