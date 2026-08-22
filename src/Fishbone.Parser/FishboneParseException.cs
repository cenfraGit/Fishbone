// --------------------------------------------------------------------------------
// FishboneParseException.cs
//
// this one exception is used in the ASTParser and AstBuilderVisitor only.
//
// var parseTree = parser.program();
// if (errorListener.Diagnostics.Count > 0)
//     throw new FishboneParseException(errorListener.Diagnostics);
//
// so if there's any errors during parsing, ASTParser throws that list
// to whoever called it so that they can report errors to the user.
//
// carries diagnostics natively and projects them to the older ParseError shape for
// callers still reading Errors.
// --------------------------------------------------------------------------------

using Fishbone.Core;

namespace Fishbone.Parser;

public class FishboneParseException : Exception, IFishboneDiagnosticSource
{
    private ParseError[]? _errors;

    public IReadOnlyList<FishboneDiagnostic> Diagnostics { get; }

    /// <summary>
    /// The syntax errors in <see cref="ParseError"/> form. Prefer
    /// <see cref="Diagnostics"/>, or <see cref="FishboneDiagnostics.From"/> if the code also
    /// has to handle runtime errors.
    /// </summary>
    public IReadOnlyList<ParseError> Errors =>
        _errors ??= [.. Diagnostics.Select(diagnostic => new ParseError(
            diagnostic.Span.Line,
            diagnostic.Span.Column,
            diagnostic.Message,
            diagnostic.OffendingText) { Span = diagnostic.Span })];

    public FishboneParseException(IReadOnlyList<FishboneDiagnostic> diagnostics)
        : base(Summarize(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    public FishboneParseException(IReadOnlyList<ParseError> errors)
        : this([.. errors.Select(error => new FishboneDiagnostic(
            DiagnosticStage.Parse,
            DiagnosticSeverity.Error,
            error.Message,
            error.Span) { OffendingText = error.OffendingText })])
    {
    }

    // a single error is worth spelling out in the message, since a console client may print
    // only that. several are left to the caller to enumerate
    private static string Summarize(IReadOnlyList<FishboneDiagnostic> diagnostics) =>
        diagnostics.Count == 1
            ? $"Line {diagnostics[0].Span.Line}, column {diagnostics[0].Span.Column}: {diagnostics[0].Message}"
            : $"{diagnostics.Count} syntax errors found";
}