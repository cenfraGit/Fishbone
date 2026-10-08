using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using System;

namespace SpineIDE.Views.Editor;

/// <summary>Shows a shared <see cref="FishboneCompletionItem"/> in AvaloniaEdit's completion popup.</summary>
public sealed class FishboneCompletionData(FishboneCompletionItem item) : ICompletionData
{
    // no icons for now; the interface requires the member
    public IImage Image => null!;

    public string Text => item.Text;

    /// <summary>Shown in the list row.</summary>
    public object Content => item.Text;

    /// <summary>Shown as the hover tooltip (signature / kind).</summary>
    public object Description => item.Description;

    public double Priority => item.Priority;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, Text);
}
