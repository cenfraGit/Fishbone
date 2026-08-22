// --------------------------------------------------------------------------------
// SourceSpan.cs
//
// a region of source text, used by ast nodes and diagnostics.
//
// before this existed both only carried a single line/column point, so a client
// could place a caret but could not size an underline. the span carries the end
// too, which antlr already knows and used to throw away.
// --------------------------------------------------------------------------------

namespace Fishbone.Core;

/// <summary>
/// A region of source text. <see cref="Line"/> and <see cref="Column"/> are 1-based, so
/// (1, 1) is the first character of the file. <see cref="EndColumn"/> is exclusive: it
/// points one past the last character, so a single-line span's length is
/// <c>EndColumn - Column</c>. All four values are zero when the position is unknown.
/// </summary>
public readonly record struct SourceSpan(int Line, int Column, int EndLine, int EndColumn)
{
    /// <summary>An unknown position. Every component is zero.</summary>
    public static readonly SourceSpan None = default;

    /// <summary>A zero-width span, for when only the start is known.</summary>
    public SourceSpan(int line, int column) : this(line, column, line, column) { }

    /// <summary>True when this span points somewhere, as opposed to <see cref="None"/>.</summary>
    public bool IsKnown => Line > 0;

    /// <summary>
    /// True when the span covers a nonzero amount of text on a single line, which is the case a
    /// client can underline without needing the source text to measure against.
    /// </summary>
    public bool IsSingleLine => IsKnown && Line == EndLine && EndColumn > Column;

    public override string ToString() =>
        IsKnown ? $"line {Line}, column {Column}" : "unknown location";
}