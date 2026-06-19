using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using SpineIDE.Models.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorView : UserControl
{
    private TextMate.Installation? _textMateInstallation = null;

    public ScriptEditorView()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<MessageEditorAction>(this, (r, m) =>
        {
            switch (m.Action)
            {
                case EditorAction.Copy:  Editor.Copy(); break;
                case EditorAction.Cut:   Editor.Cut(); break;
                case EditorAction.Paste: Editor.Paste(); break;
                case EditorAction.Undo:  Editor.Undo(); break;
                case EditorAction.Redo:  Editor.Redo(); break;
                case EditorAction.AddLineComment: ToggleLineComment(true); break;
                case EditorAction.RemoveLineComment: ToggleLineComment(false); break;
            }
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var editor = this.FindControl<TextEditor>("Editor");
        if (editor != null && _textMateInstallation == null)
        {
            var registryOptions = new FishboneRegistryOptions();
            _textMateInstallation = editor.InstallTextMate(registryOptions);
            _textMateInstallation.SetGrammar("source.fb");

            editor.Options.ConvertTabsToSpaces = true;
            editor.Options.IndentationSize = 4;
            editor.TextArea.IndentationStrategy = new CSharpIndentationStrategy(editor.Options);
        }
    }

    private void ToggleLineComment(bool addComment)
    {
        var document = Editor.Document;
        // var selection = Editor.SelectedText;

        int lineStart = document.GetLineByOffset(Editor.SelectionStart).LineNumber;
        int lineEnd = document.GetLineByOffset(Editor.SelectionStart + Editor.SelectionLength).LineNumber;

        // for single undo
        using (document.RunUpdate())
        {
            for (int i = lineEnd; i >= lineStart; i--)
            {
                var line = document.GetLineByNumber(i);
                string lineText = document.GetText(line.Offset, line.TotalLength);

                if (addComment)
                    document.Insert(line.Offset, "// ");
                else
                {
                    int commentIndex = lineText.IndexOf("//");
                    if (commentIndex != -1)
                    {
                        var prefix = lineText.Substring(0, commentIndex);
                        if (string.IsNullOrWhiteSpace(prefix))
                        {
                            int lengthToRemove = (lineText.Length > commentIndex + 2 && lineText[commentIndex + 2] == ' ') ? 3 : 2;
                            document.Remove(line.Offset + commentIndex, lengthToRemove);
                        }
                    }
                }
            }
        }
    }
}