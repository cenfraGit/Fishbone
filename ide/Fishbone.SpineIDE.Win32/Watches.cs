using Fishbone;
using Fishbone.DebugClient;
using Fishbone.Debugging;
using SpineIDE.Services;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint SCI_SETHSCROLLBAR = 2130, SCI_SETVSCROLLBAR = 2280, SCI_SETMARGINLEFT = 2155, SCI_SETEXTRAASCENT = 2525;
    private const int TVN_KEYDOWN = -412, VK_DELETE = 0x2E;
    private static readonly IntPtr TVI_FIRST = -0xFFFF;
    private static readonly TimeSpan WatchTimeout = TimeSpan.FromSeconds(10);

    // the line a watch is typed into, and the watches, in the order they were added. they show
    // in a group of their own at the top of the variables
    private static IntPtr _watchInput, _watchLabel, _watchRoot;
    private static readonly List<string> _watches = [];
    // the watches that have a value, by expression, so the preview can follow them like variables
    private static readonly Dictionary<string, VariableNode> _watchNodes = [];
    // the finished run's values, which watches are evaluated on when the debugger isn't paused
    private static FishboneEnvironment? _finalEnvironment;
    // bumped on every refresh, so a slow one that finishes late doesn't replace a newer one
    private static int _watchVersion;
    // the line a watch is read at for its parameter tip: where the script is paused, or after it
    private static int _watchLine = int.MaxValue;

    private static IntPtr WatchSci(uint msg, nint wParam = 0, nint lParam = 0) => SendMessageW(_watchInput, msg, wParam, lParam);

    private static IntPtr WatchSci(uint msg, nint wParam, byte[] lParam) => SendMessageW(_watchInput, msg, wParam, lParam);

    // a one line scintilla, so typing a watch has the editor's completion list
    private static void SetupWatchInput()
    {
        _watchLabel = CreateChild("STATIC", "Watch", WS_CHILD | WS_VISIBLE | SS_CENTERIMAGE, 0);
        _watchInput = CreateChild("Scintilla", "", WS_CHILD | WS_VISIBLE | WS_BORDER, 0);
        WatchSci(SCI_SETCODEPAGE, 65001); // utf-8
        WatchSci(SCI_STYLESETFONT, STYLE_DEFAULT, Utf8("Consolas"));
        WatchSci(SCI_STYLESETSIZE, STYLE_DEFAULT, 10);
        WatchSci(SCI_STYLECLEARALL);
        for (int margin = 0; margin < 5; margin++)
            WatchSci(SCI_SETMARGINWIDTHN, margin, 0);
        WatchSci(SCI_SETHSCROLLBAR, 0);
        WatchSci(SCI_SETVSCROLLBAR, 0);
        WatchSci(SCI_AUTOCSETIGNORECASE, 1);
        WatchSci(SCI_AUTOCSETORDER, 1);
    }

    private static void SetWatchInputDpi()
    {
        SendMessageW(_watchLabel, WM_SETFONT, _guiFont, 1);
        WatchSci(SCI_SETMARGINLEFT, 0, Scale(4));
        // the text sits a little lower, in the middle of the box
        WatchSci(SCI_SETEXTRAASCENT, Scale(3));
    }

    private static string WatchText()
    {
        int length = (int)WatchSci(SCI_GETTEXTLENGTH);
        var bytes = new byte[length + 1];
        WatchSci(SCI_GETTEXT, length + 1, bytes);
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    // Enter adds what's typed as a watch
    private static void AddWatch()
    {
        string watch = WatchText().ReplaceLineEndings(" ").Trim();
        WatchSci(SCI_SETTEXT, 0, Utf8(""));
        if (watch.Length == 0 || _watches.Contains(watch))
            return;
        _watches.Add(watch);
        RefreshWatches();
    }

    // Delete on a watch takes it out
    private static void RemoveWatch(string watch)
    {
        if (_watches.Remove(watch))
            RefreshWatches();
    }

    // the watches, worked out where the script is: paused in the debugger, or finished, where the
    // final values are here. otherwise they show without values. then runs once they're in the tree
    private static async void RefreshWatches(Action? then = null)
    {
        int version = ++_watchVersion, treeVersion = _treeVersion;
        string[] watches = [.. _watches];
        IFishboneDebugClientSession? session = _pausedSession;
        FishboneEnvironment? environment = _finalEnvironment;
        FishboneConfiguration? configuration = _finalConfiguration;

        var results = new List<(string Watch, VariableNode? Node, string? Error)>();
        // the debug session ended: the watches keep the values they had
        bool ended = session is { State: not FishboneDebugSessionState.Paused };
        foreach (string watch in watches)
        {
            if (ended)
            {
                results.Add(_watchNodes.TryGetValue(watch, out VariableNode? last) ? (watch, last, null) : (watch, null, "the debug session ended"));
                continue;
            }
            try
            {
                if (session is not null)
                {
                    FishboneDebugVariable variable = await session.EvaluateAsync(watch);
                    results.Add((watch, new VariableNode { Name = watch, IsWatch = true, IsImage = variable.ImageHandle is not null, Debug = variable }, null));
                }
                else if (environment is not null)
                {
                    var (value, isImage) = await Task.Run(() =>
                    {
                        using var timeout = new CancellationTokenSource(WatchTimeout);
                        object? value = FishboneExpression.Evaluate(watch, environment, configuration, timeout.Token);
                        return (value, configuration?.CanVisualize(value) == true);
                    });
                    results.Add((watch, new VariableNode { Name = watch, IsWatch = true, IsImage = isImage, Value = value }, null));
                }
                else
                    results.Add((watch, null, null));
            }
            catch (OperationCanceledException)
            {
                results.Add((watch, null, $"it took longer than {WatchTimeout.TotalSeconds:F0} seconds"));
            }
            catch (FishboneParseException exception) when (exception.Errors.Count > 0)
            {
                results.Add((watch, null, $"column {exception.Errors[0].Column}: {exception.Errors[0].Message}"));
            }
            catch (Exception exception)
            {
                results.Add((watch, null, exception.Message));
            }
        }

        Post(() =>
        {
            if (treeVersion != _treeVersion)
                return;
            if (version == _watchVersion)
                ShowWatches(results);
            then?.Invoke();
        });
    }

    private static void ShowWatches(List<(string Watch, VariableNode? Node, string? Error)> results)
    {
        RemoveWatchRoot();
        if (results.Count == 0)
            return;

        _watchRoot = InsertItem(TVI_ROOT, "Watch", hasChildren: true, node: null, bold: true, after: TVI_FIRST);
        foreach (var (watch, node, error) in results)
        {
            if (node is null)
            {
                // no value: not paused yet, or the reason it failed
                InsertItem(_watchRoot, error is null ? watch : $"{watch}   ({error})", hasChildren: false,
                    new VariableNode { Name = watch, IsWatch = true });
                continue;
            }
            _watchNodes[watch] = node;
            if (node.Debug is { } variable)
                InsertVariableItem(_watchRoot, ItemText(watch, variable.Value, variable.Type, node.IsImage),
                    hasChildren: variable.ChildrenHandle is not null, node, checkable: true);
            else
                InsertVariableItem(_watchRoot, ItemText(watch, DebugValueFormatter.FormatValue(node.Value), DebugValueFormatter.FormatType(node.Value), node.IsImage),
                    hasChildren: node.Value is IDictionary or (IEnumerable and not string) || LocalImages(node) is not null, node, checkable: true);
        }
        SendMessageW(_variables, TVM_EXPAND, (nint)TVE_EXPAND, _watchRoot);
    }

    // a watch's checkbox goes with it, so the preview no longer finds it there
    private static void RemoveWatchRoot()
    {
        _watchNodes.Clear();
        if (_watchRoot == 0)
            return;
        foreach (var (item, node) in _checkNodes.Where(pair => pair.Value.IsWatch).ToList())
        {
            _checkNodes.Remove(item);
            _checkItems.Remove(node.Name);
        }
        SendMessageW(_variables, TVM_DELETEITEM, 0, _watchRoot);
        _watchRoot = 0;
    }

    private static void OnWatchInputNotification(SCNotification notification)
    {
        switch (notification.code)
        {
            case SCN_CHARADDED when char.IsLetter((char)notification.ch) || notification.ch is '_' or '.':
                ShowWatchCompletion(forced: false);
                break;
            case SCN_UPDATEUI when (notification.updated & 0x3) != 0: // SC_UPDATE_CONTENT | SC_UPDATE_SELECTION
                UpdateWatchSignatureHelp();
                break;
            case SCN_CALLTIPCLICK:
                CycleOverload((int)notification.position);
                break;
        }
    }

    // like the editor's list. paused, the debugger offers what's in scope where the script
    // stopped, and the members of values. otherwise the script's names and the final values
    private static async void ShowWatchCompletion(bool forced)
    {
        int caretBytes = (int)WatchSci(SCI_GETCURRENTPOS);
        int wordStart = (int)WatchSci(SCI_WORDSTARTPOSITION, caretBytes, 1);
        bool afterDot = wordStart > 0 && WatchSci(SCI_GETCHARAT, wordStart - 1) == '.';
        if (!forced && !(afterDot ? caretBytes - wordStart <= 1 : caretBytes - wordStart == 1))
            return;

        string text = WatchText();
        int caret = Encoding.UTF8.GetCharCount(Encoding.UTF8.GetBytes(text), 0, caretBytes);
        int start;
        IEnumerable<string> items;
        if (_pausedSession is { } session)
        {
            FishboneDebugCompletions? completions;
            try
            {
                completions = await session.GetCompletionsAsync(text, caret);
            }
            catch
            {
                return;
            }
            if (completions is null)
                return;
            (start, items) = (completions.Start, completions.Items);
        }
        else if (_remoteName is null && (_analysis ??= Analyze()) is { } analysis)
        {
            FishboneEnvironment? environment = _finalEnvironment;
            FishboneConfiguration? configuration = _finalConfiguration;
            Func<string, object?>? evaluate = environment is null ? null
                : expression => FishboneExpression.Evaluate(expression, environment, configuration);
            if (analysis.WatchCompletionsAt(Encoding.UTF8.GetString(GetEditorBytes()), int.MaxValue, text, caret, evaluate) is not { } completions)
                return;
            (start, items) = (completions.Start, completions.Items.Select(item => item.Text));
        }
        else
            return;

        Post(() =>
        {
            // the debugger can answer after more was typed. while that's the same word, the list
            // still fits, narrowed to what's typed now
            string now = WatchText();
            int caretNow = Encoding.UTF8.GetCharCount(Encoding.UTF8.GetBytes(now), 0, (int)WatchSci(SCI_GETCURRENTPOS));
            if (!now.StartsWith(text[..start], StringComparison.Ordinal) || caretNow < start
                || !now[start..caretNow].All(c => char.IsLetterOrDigit(c) || c == '_'))
                return;
            string list = string.Join(' ', items.Where(name => !name.Contains(' ')));
            WatchSci(SCI_AUTOCSHOW, Encoding.UTF8.GetByteCount(now.AsSpan(start, caretNow - start)), Utf8(list));
        });
    }

    // the delete key on a watch's row
    private static void OnVariablesKeyDown(IntPtr lParam)
    {
        int key = Marshal.ReadInt16(lParam, Marshal.SizeOf<NMHDR>());
        if (key == VK_DELETE && _selectedNode is { IsWatch: true } watch)
            RemoveWatch(watch.Name);
    }
}
