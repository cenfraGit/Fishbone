using Fishbone.Core;
using SpineIDE.Views.Editor;

namespace SpineIDE.Tests;

// the editor keeps syntax and runtime diagnostics in separate buckets with different lifetimes.
// these cover the rules that keep one from clobbering the other, and the clear-on-edit behaviour
// that is what makes anchoring unnecessary.
public class EditorDiagnosticStateTests
{
    private static ScriptEditorVM Editor(string contents = "let x = missing;") =>
        new("test.fb", null, contents);

    private static FishboneDiagnostic Runtime(int line, int column, int endColumn, string message = "boom") =>
        new(DiagnosticStage.Runtime, DiagnosticSeverity.Error, message,
            new SourceSpan(line, column, line, endColumn));

    private static FishboneDiagnostic Parse(int line, int column, int endColumn, string message = "syntax") =>
        new(DiagnosticStage.Parse, DiagnosticSeverity.Error, message,
            new SourceSpan(line, column, line, endColumn));

    [Fact]
    public void NewEditor_HasNoDiagnostics()
    {
        Assert.Empty(Editor().DiagnosticSegments);
    }

    [Fact]
    public void RuntimeDiagnostics_ProduceASegment()
    {
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);

        var segment = Assert.Single(editor.DiagnosticSegments);
        Assert.Equal(7, segment.Length);   // "missing"
    }

    [Fact]
    public void RunChannel_IgnoresNonRuntimeStages()
    {
        // a run that failed to parse reports the same syntax errors the editor already shows, so
        // underlining them from both channels would double up
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Parse(1, 1, 4), Runtime(1, 9, 16)]);

        Assert.Single(editor.DiagnosticSegments);
    }

    [Fact]
    public void FirstEdit_RetiresRuntimeDiagnostics()
    {
        // the error describes a program that no longer exists, so it goes rather than drifting
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);
        Assert.NotEmpty(editor.DiagnosticSegments);

        editor.ScriptDocument.Insert(0, "// a comment\n");

        Assert.Empty(editor.DiagnosticSegments);
    }

    [Fact]
    public void FirstEdit_DoesNotRetireSyntaxDiagnostics()
    {
        // syntax diagnostics are replaced by the next parse, not invalidated by the edit itself
        var editor = Editor();
        editor.SetSyntaxDiagnostics([Parse(1, 1, 4)]);

        editor.ScriptDocument.Insert(0, "x");

        Assert.NotEmpty(editor.DiagnosticSegments);
    }

    [Fact]
    public void SyntaxDiagnostics_SuppressRuntimeOnes()
    {
        // if the file no longer parses, the last run's errors are stale by definition
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);
        editor.SetSyntaxDiagnostics([Parse(1, 1, 4)]);

        var segment = Assert.Single(editor.DiagnosticSegments);
        Assert.Equal(3, segment.Length);   // "let", the syntax one
    }

    [Fact]
    public void ClearingSyntaxDiagnostics_BringsRuntimeOnesBack()
    {
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);
        editor.SetSyntaxDiagnostics([Parse(1, 1, 4)]);
        editor.SetSyntaxDiagnostics([]);

        var segment = Assert.Single(editor.DiagnosticSegments);
        Assert.Equal(7, segment.Length);   // "missing" is visible again
    }

    [Fact]
    public void ClearRuntimeDiagnostics_LeavesSyntaxAlone()
    {
        var editor = Editor();
        editor.SetSyntaxDiagnostics([Parse(1, 1, 4)]);
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);

        editor.ClearRuntimeDiagnostics();

        Assert.Single(editor.DiagnosticSegments);
    }

    [Fact]
    public void DiagnosticsChanged_FiresSoTheEditorRepaints()
    {
        var editor = Editor();
        int fired = 0;
        editor.DiagnosticsChanged += (_, _) => fired++;

        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);
        editor.SetSyntaxDiagnostics([Parse(1, 1, 4)]);

        Assert.Equal(2, fired);
    }

    [Fact]
    public void DocumentSwap_DropsRuntimeDiagnostics()
    {
        // offsets from the old document mean nothing in the new one
        var editor = Editor();
        editor.SetRuntimeDiagnostics([Runtime(1, 9, 16)]);

        editor.ScriptDocument = new AvaloniaEdit.Document.TextDocument("let y = 2;");

        Assert.Empty(editor.DiagnosticSegments);
    }

    [Fact]
    public void UnknownSpan_ProducesNoSegment()
    {
        // a plugin-load failure or a foreign exception has nowhere to point
        var editor = Editor();
        editor.SetRuntimeDiagnostics(
            [new FishboneDiagnostic(DiagnosticStage.Runtime, DiagnosticSeverity.Error, "no location", SourceSpan.None)]);

        Assert.Empty(editor.DiagnosticSegments);
    }
}