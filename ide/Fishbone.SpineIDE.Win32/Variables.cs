using Fishbone;
using Fishbone.DebugClient;
using Fishbone.Debugging;
using System.Collections;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint TVS_HASBUTTONS = 0x1, TVS_HASLINES = 0x2, TVS_LINESATROOT = 0x4, TVS_SHOWSELALWAYS = 0x20;
    private const uint TVM_DELETEITEM = 0x1101, TVM_EXPAND = 0x1102, TVM_INSERTITEMW = 0x1132;
    private const uint TVIF_TEXT = 0x1, TVIF_PARAM = 0x4, TVIF_CHILDREN = 0x40, TVE_EXPAND = 0x2;
    private const int TVN_ITEMEXPANDINGW = -454;
    private static readonly IntPtr TVI_ROOT = -0x10000, TVI_LAST = -0xFFFE;

    // what a tree item stands for. a pause hands out debug variables, whose children come from the
    // debug host. a finished run hands out the real values, whose children are read directly
    private sealed class VariableNode
    {
        public FishboneDebugVariable? Debug { get; init; }
        public object? Value { get; init; }
        public bool Loaded { get; set; }
    }

    private static readonly Dictionary<IntPtr, VariableNode> _nodes = [];
    private static IFishboneDebugClientSession? _pausedSession;
    private static int _nextNode = 1;

    // bumped whenever the tree is refilled, so children that finish loading late are dropped
    private static int _treeVersion;

    private static void ClearVariables()
    {
        SendMessageW(_variables, TVM_DELETEITEM, 0, TVI_ROOT);
        _nodes.Clear();
        _pausedSession = null;
        _treeVersion++;
    }

    private static void ShowDebugVariables(FishboneDebugFrame? frame, IFishboneDebugClientSession session)
    {
        ClearVariables();
        _pausedSession = session;
        if (frame is null)
            return;

        foreach (FishboneDebugScope scope in frame.Scopes)
        {
            IntPtr scopeItem = InsertItem(TVI_ROOT, scope.Name, hasChildren: scope.Variables.Length > 0, node: null);
            foreach (FishboneDebugVariable variable in scope.Variables)
                InsertDebugVariable(scopeItem, variable);
            SendMessageW(_variables, TVM_EXPAND, (nint)TVE_EXPAND, scopeItem);
        }
    }

    // after a run without the debugger, the script's own variables. functions registered by the
    // host are left out, the same as in the Avalonia SpineIDE
    private static void ShowFinalVariables(FishboneEnvironment environment)
    {
        ClearVariables();
        foreach (var (name, value) in environment.Values)
            if (value is not Delegate)
                InsertLocalValue(TVI_ROOT, name, value);
    }

    private static void InsertDebugVariable(IntPtr parent, FishboneDebugVariable variable) =>
        InsertItem(parent, ItemText(variable.Name, variable.Value, variable.Type)
                + (variable.ImageHandle is null ? "" : "    double-click to view"),
            hasChildren: variable.ChildrenHandle is not null, new VariableNode { Debug = variable });

    private static void InsertLocalValue(IntPtr parent, string name, object? value) =>
        InsertItem(parent, ItemText(name, DebugValueFormatter.FormatValue(value), DebugValueFormatter.FormatType(value)),
            hasChildren: value is IDictionary or (IEnumerable and not string), new VariableNode { Value = value });

    private static string ItemText(string name, string value, string? type) =>
        string.IsNullOrEmpty(type) ? $"{name} = {value}" : $"{name} = {value}    ({type})";

    private static IntPtr InsertItem(IntPtr parent, string text, bool hasChildren, VariableNode? node)
    {
        IntPtr id = 0;
        if (node is not null)
        {
            id = _nextNode++;
            _nodes[id] = node;
        }
        // the tree copies the text, so it can be freed right after
        IntPtr textPointer = Marshal.StringToHGlobalUni(text);
        try
        {
            var insert = new TVINSERTSTRUCTW
            {
                hParent = parent,
                hInsertAfter = TVI_LAST,
                item = new TVITEMW
                {
                    mask = TVIF_TEXT | TVIF_PARAM | TVIF_CHILDREN,
                    pszText = textPointer,
                    cChildren = hasChildren ? 1 : 0,
                    lParam = id
                }
            };
            return SendMessageW(_variables, TVM_INSERTITEMW, 0, ref insert);
        }
        finally
        {
            Marshal.FreeHGlobal(textPointer);
        }
    }

    // children load the first time an item opens
    private static void OnVariablesNotification(IntPtr lParam)
    {
        if (Marshal.PtrToStructure<NMHDR>(lParam).code == NM_DBLCLK)
        {
            OnVariableDoubleClick();
            return;
        }
        var notification = Marshal.PtrToStructure<NMTREEVIEWW>(lParam);
        if (notification.hdr.code != TVN_ITEMEXPANDINGW || notification.action != TVE_EXPAND)
            return;
        if (!_nodes.TryGetValue(notification.itemNew.lParam, out VariableNode? node) || node.Loaded)
            return;

        node.Loaded = true;
        IntPtr item = notification.itemNew.hItem;
        if (node.Debug?.ChildrenHandle is { } handle && _pausedSession is { } session)
            LoadDebugChildren(item, session, handle);
        else if (node.Value is IDictionary dictionary)
            foreach (DictionaryEntry entry in dictionary)
                InsertLocalValue(item, $"[{DebugValueFormatter.FormatDictionaryKey(entry.Key)}]", entry.Value);
        else if (node.Value is IEnumerable values)
        {
            int index = 0;
            foreach (object? value in values)
                InsertLocalValue(item, $"[{index++}]", value);
        }
    }

    private static async void LoadDebugChildren(IntPtr item, IFishboneDebugClientSession session, FishboneVariableHandle handle)
    {
        int version = _treeVersion;
        try
        {
            IReadOnlyList<FishboneDebugVariable> children = await session.GetVariablesAsync(handle);
            Post(() =>
            {
                if (version != _treeVersion)
                    return;
                foreach (FishboneDebugVariable child in children)
                    InsertDebugVariable(item, child);
                SendMessageW(_variables, TVM_EXPAND, (nint)TVE_EXPAND, item);
            });
        }
        catch (InvalidOperationException)
        {
            // the script moved on, so the debug host no longer has these values
            Post(() =>
            {
                if (version == _treeVersion)
                    InsertItem(item, "(gone, the script continued)", hasChildren: false, node: null);
            });
        }
    }
}
