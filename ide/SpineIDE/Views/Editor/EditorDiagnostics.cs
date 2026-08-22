// --------------------------------------------------------------------------------
// EditorDiagnostics.cs
//
// draws wavy underlines under the spans a diagnostic points at.
//
// this is a background renderer, like PausedLineRenderer, which means it paints
// underneath the glyphs. painting on top would need a custom layer inserted above
// KnownLayer.Text: a background renderer at KnownLayer.Text is not enough, because
// the text layer holds its glyphs as child visuals and a visual's own drawing
// composites before its children. the cost of staying a background renderer is
// cosmetic and bounded (the wave sits under descenders and under the selection
// highlight), and it keeps the pointer-input and invalidation behaviour that
// TextView already handles for us.
//
// the diagnostics themselves come from the view model, read through a provider on
// every paint rather than captured once. that matters because Dock clears the
// DataContext of an inactive tab and the view falls back to a shared static empty
// document, so anything cached here would leak one tab's squiggles into another.
// --------------------------------------------------------------------------------

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fishbone.Core;

namespace SpineIDE.Views.Editor;

/// <summary>A diagnostic resolved to a concrete region of the current document.</summary>
internal readonly record struct DiagnosticSegment(int StartOffset, int Length, DiagnosticSeverity Severity)
    : ISegment
{
    public int Offset => StartOffset;
    public int EndOffset => StartOffset + Length;
}

internal static class DiagnosticSpans
{
    /// <summary>
    /// Turns a 1-based <see cref="SourceSpan"/> into an offset range in <paramref name="document"/>,
    /// clamping anything out of bounds. A span can outlive the text it described (a runtime error
    /// still on screen while the document shrinks), so nothing here may assume it still fits.
    /// Returns false when there is nothing meaningful to underline.
    /// </summary>
    public static bool TryResolve(TextDocument document, SourceSpan span, out int start, out int length)
    {
        start = 0;
        length = 0;

        if (!span.IsKnown || document.TextLength == 0)
            return false;

        int line = Math.Clamp(span.Line, 1, document.LineCount);
        DocumentLine documentLine = document.GetLineByNumber(line);

        // GetOffset clamps the column but not the line, hence the clamp above. EndColumn is
        // exclusive, so it maps straight through with no adjustment
        start = document.GetOffset(line, Math.Max(span.Column, 1));

        int end = span.EndLine == line
            ? document.GetOffset(line, Math.Max(span.EndColumn, 1))
            // a multi-line span stops at the end of its first line. the alternative underlines
            // whole blocks, because a statement-level error reports the statement's own node
            : documentLine.EndOffset;

        if (end <= start)
            return TryResolveEmpty(document, ref start, out length);

        start = Math.Clamp(start, 0, document.TextLength);
        length = Math.Clamp(end, start, document.TextLength) - start;
        return length > 0;
    }

    // a zero-width span has nothing to underline: an end-of-file error points at the position
    // after the last character. walk back to the nearest real character, stepping over line
    // endings, so the mark lands on text instead of floating in empty space
    private static bool TryResolveEmpty(TextDocument document, ref int start, out int length)
    {
        length = 0;
        int index = Math.Clamp(start, 0, document.TextLength);

        while (index > 0 && (document.GetCharAt(index - 1) == '\n' || document.GetCharAt(index - 1) == '\r'))
            index--;

        if (index > 0)
        {
            start = index - 1;
            length = 1;
            return true;
        }

        // nothing before the position, so mark the first character instead
        if (document.TextLength > 0)
        {
            start = 0;
            length = 1;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Underlines diagnostics in the editor. Reads them from the view model on every paint through
/// <c>viewModelProvider</c>, mirroring how <see cref="BreakpointMargin"/> resolves its view model.
/// </summary>
internal sealed class DiagnosticSquiggleRenderer : IBackgroundRenderer
{
    private static readonly IBrush FallbackErrorBrush = new SolidColorBrush(Color.Parse("#E06C75"));
    private static readonly IBrush FallbackWarningBrush = new SolidColorBrush(Color.Parse("#D19A66"));

    // wave shape, in device-independent pixels. a short period reads as a squiggle rather than
    // a zigzag, and an amplitude of 2 keeps the whole wave inside the line box
    private const double WavePeriod = 4.0;
    private const double WaveAmplitude = 2.0;

    // a cascade of parse errors can produce a lot of diagnostics at once, and each one costs a
    // geometry build. the cap bounds a single paint rather than trusting the producer
    private const int MaxSquigglesPerPaint = 100;

    private readonly Func<ScriptEditorVM?> _viewModelProvider;

    public DiagnosticSquiggleRenderer(Func<ScriptEditorVM?> viewModelProvider)
    {
        _viewModelProvider = viewModelProvider;
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid)
            return;
        if (textView.Document is not { } document)
            return;
        if (_viewModelProvider() is not { } viewModel)
            return;

        // SetDocument can leave the view model swapped while Editor.Document has not caught up,
        // and an unbound tab is showing a shared empty document. either way the offsets we would
        // compute belong to a different script, so draw nothing
        if (!ReferenceEquals(viewModel.ScriptDocument, document))
            return;

        // resolved once per paint rather than per diagnostic: the brushes come from a theme
        // lookup, and repainting is where a theme change is picked up
        var errorPen = BuildPen(BreakpointMargin.ResolveBrush(
            textView, "EditorErrorSquiggleBrush", FallbackErrorBrush));
        var warningPen = BuildPen(BreakpointMargin.ResolveBrush(
            textView, "EditorWarningSquiggleBrush", FallbackWarningBrush));

        double scale = TopLevel.GetTopLevel(textView)?.RenderScaling ?? 1.0;

        int drawn = 0;
        foreach (var segment in viewModel.DiagnosticSegments)
        {
            if (drawn >= MaxSquigglesPerPaint)
                break;
            if (segment.Length <= 0 || segment.EndOffset > document.TextLength)
                continue;

            Pen pen = segment.Severity == DiagnosticSeverity.Warning ? warningPen : errorPen;

            foreach (Rect rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                if (rect.Width < 1.0)
                    continue;
                DrawWave(drawingContext, rect, pen, scale);
            }

            drawn++;
        }
    }

    private static Pen BuildPen(IBrush brush) =>
        new(brush, 1.0) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static void DrawWave(DrawingContext drawingContext, Rect rect, Pen pen, double scale)
    {
        // put the 1px pen on a pixel centre so the wave stays crisp; only the vertical needs it
        double baseline = Math.Round((rect.Bottom - 1.0) * scale) / scale - 0.5 / scale;
        double crest = baseline - WaveAmplitude;

        // anchor the phase to the document, not to the segment, so the wave does not visibly
        // shift when the segment start moves or the view scrolls sideways
        double startX = Math.Floor(rect.Left / WavePeriod) * WavePeriod;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(startX, baseline), isFilled: false);
            bool up = true;
            for (double x = startX; x < rect.Right; x += WavePeriod / 2.0)
            {
                context.LineTo(new Point(x + WavePeriod / 2.0, up ? crest : baseline));
                up = !up;
            }
            context.EndFigure(isClosed: false);
        }

        using (drawingContext.PushClip(rect))
            drawingContext.DrawGeometry(null, pen, geometry);
    }
}