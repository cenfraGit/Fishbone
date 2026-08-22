// --------------------------------------------------------------------------------
// FishboneDiagnostic.cs
//
// one reportable problem, in a shape every client can render without knowing
// which exception type carried it.
//
// before this existed each client sniffed exception types by hand: catch
// FishboneParseException and loop its list, else catch FishboneRuntimeException and
// check whether Line is nonzero, else fall back to Message. SpineCLI and SpineIDE
// each had their own copy of that, and a third client would have written a third.
// now they call FishboneDiagnostics.From(exception) and render a list.
// --------------------------------------------------------------------------------

namespace Fishbone.Core;

/// <summary>Which part of the pipeline produced a diagnostic.</summary>
public enum DiagnosticStage
{
    /// <summary>The lexer could not turn characters into a token.</summary>
    Lex,
    /// <summary>The tokens did not fit the grammar, or the ast builder rejected them.</summary>
    Parse,
    /// <summary>The script failed while executing.</summary>
    Runtime,
    /// <summary>The host setup failed, for example a plugin that could not load.</summary>
    Configuration
}

/// <summary>How much a diagnostic matters.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Execution cannot proceed, or has already stopped.</summary>
    Error,
    /// <summary>Something worth surfacing that did not stop execution.</summary>
    Warning
}

/// <summary>
/// A single problem to report, carrying where it happened and which stage found it. This is the
/// shape clients consume; the exception types are the transport.
/// </summary>
public sealed record FishboneDiagnostic(
    DiagnosticStage Stage,
    DiagnosticSeverity Severity,
    string Message,
    SourceSpan Span)
{
    /// <summary>
    /// The source text the diagnostic is about, when the producer knew it. Useful for a client
    /// that wants to show the offending token without re-reading the script.
    /// </summary>
    public string? OffendingText { get; init; }

    /// <summary>
    /// The message as the underlying tool phrased it, kept when <see cref="Message"/> is a
    /// friendlier rewrite. Null when no rewriting happened.
    /// </summary>
    public string? RawMessage { get; init; }

    public override string ToString() =>
        Span.IsKnown ? $"{Span}: {Message}" : Message;
}

/// <summary>
/// Implemented by exceptions that carry diagnostics, so <see cref="FishboneDiagnostics.From"/>
/// can extract them without knowing the concrete type.
/// </summary>
public interface IFishboneDiagnosticSource
{
    /// <summary>The diagnostics this exception reports. Never empty.</summary>
    IReadOnlyList<FishboneDiagnostic> Diagnostics { get; }
}

/// <summary>
/// The single entry point a client uses to turn any exception from running a script into
/// something renderable.
/// </summary>
public static class FishboneDiagnostics
{
    /// <summary>
    /// Extracts the diagnostics from any exception a Fishbone run can throw. Exceptions that
    /// carry their own (parse and runtime errors) hand back what they hold, which may be several
    /// for a parse that found more than one syntax error. Anything else, including a .NET
    /// exception escaping from a host call, becomes a single runtime diagnostic with an unknown
    /// location, so a caller never has to special-case a foreign exception.
    /// </summary>
    public static IReadOnlyList<FishboneDiagnostic> From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // a single-inner AggregateException is almost always a Task boundary rather than real
        // parallelism, so unwrap it to reach the diagnostic underneath
        if (exception is AggregateException { InnerExceptions.Count: 1 } aggregate)
            return From(aggregate.InnerExceptions[0]);

        if (exception is IFishboneDiagnosticSource source && source.Diagnostics.Count > 0)
            return source.Diagnostics;

        return [Runtime(exception.Message, SourceSpan.None)];
    }

    /// <summary>Builds a runtime error diagnostic.</summary>
    public static FishboneDiagnostic Runtime(string message, SourceSpan span) =>
        new(DiagnosticStage.Runtime, DiagnosticSeverity.Error, message, span);

    /// <summary>Builds a host-configuration diagnostic, for example a plugin that failed to load.</summary>
    public static FishboneDiagnostic Configuration(string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(DiagnosticStage.Configuration, severity, message, SourceSpan.None);
}