using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using Avalonia.Threading;
using System;
using SpineIDE.Models.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorView : UserControl
{
    private static readonly FishboneRegistryOptions RegistryOptions = new();
    private static readonly TextDocument EmptyDocument = new();
    private TextMate.Installation? _textMateInstallation = null;
    private static ScriptEditorView? _activeEditor;
    private bool _isMessengerRegistered;
    private BreakpointMargin? _breakpointMargin;
    private PausedLineRenderer? _pausedLineRenderer;
    private ScriptEditorVM? _subscribedViewModel;
    private ScriptEditorVM? _documentViewModel;
    private FoldingManager? _foldingManager;
    private readonly BraceFoldingStrategy _foldingStrategy = new();
    private DispatcherTimer? _foldingTimer;

    public ScriptEditorView()
    {
        InitializeComponent();
        // Document is managed manually (not via AXAML binding) so it is never null.
        // AvaloniaEdit throws "Document is null" in mouse-event handlers; a null can arise when
        // Dock.Avalonia clears the DataContext on the inactive tab, which would propagate null
        // through an AXAML TwoWay binding.
        Editor.Document = EmptyDocument;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyEditorTheme();
        // custom-drawn adornments cache themed brushes per paint, so force them to repaint
        _breakpointMargin?.InvalidateVisual();
        Editor.TextArea.TextView.InvalidateVisual();
    }

    // keeps the editor's syntax theme in sync with the app's light/dark variant. Requires a document:
    // TextMate defers its editor model until one is bound, and applying a theme before then throws
    // "Document is null". It is re-applied from OnEditorDocumentChanged once the document arrives.
    private void ApplyEditorTheme()
    {
        if (_textMateInstallation is null || Editor.Document is null)
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
            // rebuild folding when the bound document is (re)assigned — the document may not exist yet
            // at attach time, and remote sources can swap it later
            editor.DocumentChanged += OnEditorDocumentChanged;

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

        // folding is installed independently of the one-time text-mate setup so it is restored if
        // this view is detached and re-attached (e.g. when its dock tab is floated)
        if (editor != null && _foldingTimer == null)
        {
            TryInstallFolding();
            _foldingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _foldingTimer.Tick += OnFoldingTimerTick;
            _foldingTimer.Start();
        }

        // re-apply in case the theme variant changed while this view was detached
        ApplyEditorTheme();
    }

    private void OnFoldingTimerTick(object? sender, EventArgs e)
    {
        // a late-bound document also gets folding here, so the install survives any attach ordering
        TryInstallFolding();
        UpdateFoldings();
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        // the folding manager is tied to a specific document; rebuild it against the new one
        if (_foldingManager is not null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }
        TryInstallFolding();
        // a theme applied before the document existed was skipped, so (re)apply it now
        ApplyEditorTheme();
    }

    // installs folding only once the editor actually has a document (Install throws on a null document)
    private void TryInstallFolding()
    {
        if (_foldingManager is null && Editor.Document is not null)
        {
            _foldingManager = FoldingManager.Install(Editor.TextArea);
            UpdateFoldings();
        }
    }

    // recomputes brace foldings from the current document; reading Editor.Document each time keeps it
    // correct even when the bound document is swapped (e.g. a remote source replaces the content)
    private void UpdateFoldings()
    {
        if (_foldingManager is not null && Editor.Document is { } document)
            _foldingStrategy.UpdateFoldings(_foldingManager, document);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // unsubscribe from the old VM's property changes before switching
        if (_documentViewModel is not null)
        {
            _documentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _documentViewModel = null;
        }

        if (DataContext is ScriptEditorVM vm)
        {
            _documentViewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            SetDocument(vm.ScriptDocument);
        }
        else
        {
            // DataContext cleared (e.g. Dock switching tabs): fall back to the shared empty
            // document so the TextArea never has a null Document during mouse events.
            SetDocument(EmptyDocument);
        }

        AttachDebugAdornments(Editor);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptEditorVM.ScriptDocument) && sender is ScriptEditorVM vm)
            SetDocument(vm.ScriptDocument);
    }

    private void SetDocument(TextDocument document)
    {
        if (ReferenceEquals(Editor.Document, document))
            return;

        // FoldingElementGenerator validates its document synchronously inside
        // TextArea.OnDocumentChanged (caret reset → visual line build) which fires before
        // editor.DocumentChanged gives our handler a chance to react. Uninstall first so the
        // generator is gone by the time the swap happens; OnEditorDocumentChanged reinstalls.
        if (_foldingManager is not null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

        Editor.Document = document;
    }

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

        if (_documentViewModel is not null)
        {
            _documentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _documentViewModel = null;
        }

        if (_foldingTimer is not null)
        {
            _foldingTimer.Stop();
            _foldingTimer.Tick -= OnFoldingTimerTick;
            _foldingTimer = null;
        }
        if (_foldingManager is not null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
        }

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