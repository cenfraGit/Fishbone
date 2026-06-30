using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace SpineIDE.Views.Editor;

internal sealed class BraceFoldingStrategy
{
    private const char OpeningBrace = '{';
    private const char ClosingBrace = '}';

    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, firstErrorOffset: -1);
    }

    private static IEnumerable<NewFolding> CreateNewFoldings(ITextSource document)
    {
        var foldings = new List<NewFolding>();
        var startOffsets = new Stack<int>();
        int lastNewLineOffset = 0;

        for (int i = 0; i < document.TextLength; i++)
        {
            char c = document.GetCharAt(i);
            if (c == OpeningBrace)
            {
                startOffsets.Push(i);
            }
            else if (c == ClosingBrace && startOffsets.Count > 0)
            {
                int startOffset = startOffsets.Pop();
                // only fold when the braces span more than one line
                if (startOffset < lastNewLineOffset)
                    foldings.Add(new NewFolding(startOffset, i + 1));
            }
            else if (c is '\n' or '\r')
            {
                lastNewLineOffset = i + 1;
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }
}