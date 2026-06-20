using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using System;
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
            editor.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            editor.AddHandler(InputElement.TextInputEvent, OnEditorTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        var document = Editor.Document;
        int caretOffset = Editor.CaretOffset;
        var line = document.GetLineByOffset(caretOffset);
        string lineBeforeCaret = document.GetText(line.Offset, caretOffset - line.Offset);
        string indent = FishboneEditorIndentation.IndentForNewLine(document.Text, line.Offset, caretOffset, Editor.Options.IndentationSize);
        string newline = Environment.NewLine;

        e.Handled = true;

        using (document.RunUpdate())
        {
            if (FishboneEditorIndentation.IsBetweenBraces(document.Text, caretOffset))
            {
                string closingIndent = FishboneEditorIndentation.LeadingWhitespace(lineBeforeCaret);
                string insertion = newline + indent + newline + closingIndent;
                document.Insert(caretOffset, insertion);
                Editor.CaretOffset = caretOffset + newline.Length + indent.Length;
                return;
            }

            document.Insert(caretOffset, newline + indent);
            Editor.CaretOffset = caretOffset + newline.Length + indent.Length;
        }
    }

    private void OnEditorTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != "}")
            return;

        var document = Editor.Document;
        int caretOffset = Editor.CaretOffset;
        if (caretOffset < 0)
            return;

        DocumentLine line = document.GetLineByOffset(caretOffset);
        string lineBeforeBrace = document.GetText(line.Offset, caretOffset - line.Offset);
        if (!FishboneEditorIndentation.ShouldDedentClosingBrace(lineBeforeBrace))
            return;

        string currentIndent = FishboneEditorIndentation.LeadingWhitespace(lineBeforeBrace);
        string newIndent = FishboneEditorIndentation.Dedent(currentIndent, Editor.Options.IndentationSize);
        int removeLength = currentIndent.Length - newIndent.Length;

        e.Handled = true;

        using (document.RunUpdate())
        {
            if (removeLength > 0)
                document.Remove(line.Offset, removeLength);

            int insertOffset = caretOffset - removeLength;
            document.Insert(insertOffset, "}");
            Editor.CaretOffset = insertOffset + 1;
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