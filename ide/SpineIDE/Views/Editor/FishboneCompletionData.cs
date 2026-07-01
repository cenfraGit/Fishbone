using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace SpineIDE.Views.Editor;

/// <summary>What a completion item represents; also drives sort priority in the popup.</summary>
public enum FishboneCompletionKind
{
    Keyword,
    Function,
    Type,
    Constant,
    Variable,
    Parameter
}

/// <summary>
/// A single entry in the editor's completion popup. Implements AvaloniaEdit's <see cref="ICompletionData"/>;
/// items are plain data (no per-window state) so the catalog can share one instance across every editor.
/// </summary>
public sealed class FishboneCompletionData : ICompletionData
{
    public FishboneCompletionData(string text, FishboneCompletionKind kind, string? description = null)
    {
        Text = text;
        Kind = kind;
        Description = description ?? kind.ToString().ToLowerInvariant();
        // in-scope locals rank above the global API, which ranks above bare keywords
        Priority = kind switch
        {
            FishboneCompletionKind.Variable or FishboneCompletionKind.Parameter => 3,
            FishboneCompletionKind.Function or FishboneCompletionKind.Type or FishboneCompletionKind.Constant => 2,
            _ => 1
        };
    }

    public FishboneCompletionKind Kind { get; }

    // no icons for now; the interface requires the member
    public IImage Image => null!;

    public string Text { get; }

    /// <summary>Shown in the list row.</summary>
    public object Content => Text;

    /// <summary>Shown as the hover tooltip (signature / kind).</summary>
    public object Description { get; }

    public double Priority { get; }

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, Text);
}