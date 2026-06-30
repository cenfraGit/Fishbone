using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace SpineIDE.Views.Editor;

internal sealed class BreakpointMargin : AbstractMargin
{
    private readonly TextEditor editor;
    private readonly Func<ScriptEditorVM?> viewModelProvider;
    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.Parse("#E05555"));
    private static readonly IBrush UnverifiedBreakpointBrush = new SolidColorBrush(Color.Parse("#777777"));
    private static readonly IBrush FallbackGutterBrush = new SolidColorBrush(Color.Parse("#161616"));

    public BreakpointMargin(TextEditor editor, Func<ScriptEditorVM?> viewModelProvider)
    {
        this.editor = editor;
        this.viewModelProvider = viewModelProvider;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 0);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(ResolveBrush(this, "EditorGutterBrush", FallbackGutterBrush), Bounds);
        var textView = editor.TextArea.TextView;
        if (!textView.VisualLinesValid)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            int line = visualLine.FirstDocumentLine.LineNumber;
            if (viewModelProvider() is not { } viewModel || !viewModel.HasBreakpoint(line))
                continue;

            double y = visualLine.VisualTop - textView.ScrollOffset.Y + visualLine.Height / 2;
            IBrush brush = viewModel.IsBreakpointVerified(line) ? BreakpointBrush : UnverifiedBreakpointBrush;
            context.DrawEllipse(brush, null, new Point(9, y), 5, 5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var textView = editor.TextArea.TextView;
        if (!textView.VisualLinesValid)
            return;

        double visualY = e.GetPosition(this).Y + textView.ScrollOffset.Y;
        var visualLine = textView.GetVisualLineFromVisualTop(visualY);
        if (visualLine is null || viewModelProvider() is not { } viewModel)
            return;
        if (!viewModel.CanToggleBreakpoints)
            return;

        viewModel.ToggleBreakpoint(visualLine.FirstDocumentLine.LineNumber);
        InvalidateVisual();
        e.Handled = true;
    }

    // resolves a themed brush from app resources for the control's active variant, falling back to a
    // fixed brush so rendering never depends on the resource being present
    internal static IBrush ResolveBrush(Control control, string key, IBrush fallback) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : fallback;
}

internal sealed class PausedLineRenderer : IBackgroundRenderer
{
    private static readonly IBrush FallbackPausedBrush = new SolidColorBrush(Color.Parse("#403A2A"));

    public int? Line { get; set; }
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Line is null || Line < 1 || Line > textView.Document.LineCount)
            return;

        IBrush pausedBrush = BreakpointMargin.ResolveBrush(textView, "PausedLineBrush", FallbackPausedBrush);
        DocumentLine line = textView.Document.GetLineByNumber(Line.Value);
        foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
            drawingContext.FillRectangle(pausedBrush, new Rect(0, rectangle.Top, textView.Bounds.Width, rectangle.Height));
    }
}