using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using System;
using SpineIDE.Models.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorView : UserControl
{
    private static readonly FishboneRegistryOptions RegistryOptions = new();
    private TextMate.Installation? _textMateInstallation = null;
    private static ScriptEditorView? _activeEditor;
    private bool _isMessengerRegistered;
    private BreakpointMargin? _breakpointMargin;
    private PausedLineRenderer? _pausedLineRenderer;
    private ScriptEditorVM? _subscribedViewModel;

    public ScriptEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyEditorTheme();
        // custom-drawn adornments cache themed brushes per paint, so force them to repaint
        _breakpointMargin?.InvalidateVisual();
        Editor.TextArea.TextView.InvalidateVisual();
    }

    // keeps the editor's syntax theme in sync with the app's light/dark variant
    private void ApplyEditorTheme()
    {
        if (_textMateInstallation is null)
            return;
        bool light = ActualThemeVariant == ThemeVariant.Light;
        _textMateInstallation.SetTheme(RegistryOptions.GetTheme(light));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RegisterMessengerRecipients();
        ActualThemeVariantChanged += OnThemeVariantChanged;

        var editor = this.FindControl<TextEditor>("Editor");
        if (editor != null && _textMateInstallation == null)
        {
            _textMateInstallation = editor.InstallTextMate(RegistryOptions);
            _textMateInstallation.SetGrammar("source.fb");
            ApplyEditorTheme();

            editor.Options.ConvertTabsToSpaces = true;
            editor.Options.IndentationSize = 4;
            editor.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            editor.AddHandler(InputElement.TextInputEvent, OnEditorTextInput, RoutingStrategies.Tunnel, handledEventsToo: true);
            editor.AddHandler(InputElement.GotFocusEvent, OnEditorGotFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            editor.AddHandler(InputElement.PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            editor.TextArea.AddHandler(InputElement.GotFocusEvent, OnEditorGotFocus, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            editor.TextArea.AddHandler(InputElement.PointerPressedEvent, OnEditorPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
            AttachDebugAdornments(editor);
            _activeEditor = this;
        }

        // re-apply in case the theme variant changed while this view was detached
        ApplyEditorTheme();
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => AttachDebugAdornments(Editor);

    private void AttachDebugAdornments(TextEditor editor)
    {
        if (_breakpointMargin is not null || DataContext is not ScriptEditorVM viewModel)
            return;

        _breakpointMargin = new BreakpointMargin(editor, () => DataContext as ScriptEditorVM);
        _subscribedViewModel = viewModel;
        _subscribedViewModel.BreakpointVisualsChanged += OnBreakpointVisualsChanged;
        editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        _pausedLineRenderer = new PausedLineRenderer();
        editor.TextArea.TextView.BackgroundRenderers.Add(_pausedLineRenderer);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        ActualThemeVariantChanged -= OnThemeVariantChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _isMessengerRegistered = false;

        if (ReferenceEquals(_activeEditor, this))
            _activeEditor = null;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.BreakpointVisualsChanged -= OnBreakpointVisualsChanged;
    }

    private void OnBreakpointVisualsChanged(object? sender, EventArgs e) => _breakpointMargin?.InvalidateVisual();

    private void RegisterMessengerRecipients()
    {
        if (_isMessengerRegistered)
            return;

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

        WeakReferenceMessenger.Default.Register<MessageInsertSnippet>(this, (r, m) =>
        {
            if (!ReferenceEquals(_activeEditor, this))
                return;

            InsertSnippet(m.Text);
        });

        WeakReferenceMessenger.Default.Register<MessageDebugLocationChanged>(this, (r, m) =>
        {
            if (DataContext is not ScriptEditorVM viewModel || viewModel.SourceId != m.SourceId)
                return;

            if (_pausedLineRenderer is not null)
                _pausedLineRenderer.Line = m.Line;
            Editor.TextArea.TextView.InvalidateVisual();
            if (m.Line is int line)
                Editor.ScrollToLine(line);
        });

        _isMessengerRegistered = true;
    }

    private void OnEditorGotFocus(object? sender, RoutedEventArgs e)
    {
        _activeEditor = this;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _activeEditor = this;
    }

    private void InsertSnippet(string template)
    {
        if (Editor.IsReadOnly)
            return;
        var document = Editor.Document;
        int insertOffset = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
        var line = document.GetLineByOffset(insertOffset);
        string lineBeforeInsert = document.GetText(line.Offset, insertOffset - line.Offset);
        string lineIndent = FishboneEditorIndentation.LeadingWhitespace(lineBeforeInsert);
        var snippet = FishboneSnippets.Prepare(template, lineIndent);

        using (document.RunUpdate())
        {
            if (Editor.SelectionLength > 0)
                document.Remove(Editor.SelectionStart, Editor.SelectionLength);

            document.Insert(insertOffset, snippet.Text);
            Editor.CaretOffset = insertOffset + snippet.CaretOffset;
        }

        Editor.Focus();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (Editor.IsReadOnly)
            return;
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
        if (Editor.IsReadOnly)
            return;
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
        if (Editor.IsReadOnly)
            return;
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