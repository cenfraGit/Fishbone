using Fishbone;
using Fishbone.DebugClient;
using Fishbone.Debugging;
using System.Collections;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint TVS_HASBUTTONS = 0x1, TVS_LINESATROOT = 0x4, TVS_SHOWSELALWAYS = 0x20, TVS_FULLROWSELECT = 0x1000;
    private const uint TVM_DELETEITEM = 0x1101, TVM_EXPAND = 0x1102, TVM_SETITEMHEIGHT = 0x111B, TVM_SETEXTENDEDSTYLE = 0x112C,
        TVM_INSERTITEMW = 0x1132, TVS_EX_DOUBLEBUFFER = 0x4;
    private const uint TVIF_TEXT = 0x1, TVIF_STATE = 0x8, TVIF_PARAM = 0x4, TVIF_CHILDREN = 0x40, TVIS_BOLD = 0x10, TVE_EXPAND = 0x2;
    private const int TVN_SELCHANGEDW = -451, TVN_ITEMEXPANDINGW = -454;
    private static readonly IntPtr TVI_ROOT = -0x10000, TVI_LAST = -0xFFFE;

    // what a tree item stands for. a pause hands out debug variables, whose children come from the
    // debug host. a finished run hands out the real values, whose children are read directly
    private sealed class VariableNode
    {
        public required string Name { get; init; }
        public bool IsImage { get; init; }
        public FishboneDebugVariable? Debug { get; init; }
        public object? Value { get; init; }
        public bool Loaded { get; set; }
    }

    private static readonly Dictionary<IntPtr, VariableNode> _nodes = [];
    private static IFishboneDebugClientSession? _pausedSession;
    // the finished run's configuration, so its final values can still be shown as images
    private static FishboneConfiguration? _finalConfiguration;
    private static int _nextNode = 1;
    private static VariableNode? _selectedNode;

    // bumped whenever the tree is refilled, so children that finish loading late are dropped
    private static int _treeVersion;

    private static void ClearVariables()
    {
        SendMessageW(_variables, TVM_DELETEITEM, 0, TVI_ROOT);
        _nodes.Clear();
        _selectedNode = null;
        _pausedSession = null;
        _finalConfiguration = null;
        _treeVersion++;
    }

    private static void ShowDebugVariables(FishboneDebugFrame? frame, IFishboneDebugClientSession session)
    {
        ClearVariables();
        _pausedSession = session;
        if (frame is null)
        {
            ClearPreview();
            return;
        }

        foreach (FishboneDebugScope scope in frame.Scopes)
        {
            IntPtr scopeItem = InsertItem(TVI_ROOT, scope.Name, hasChildren: scope.Variables.Length > 0, node: null, bold: true);
            foreach (FishboneDebugVariable variable in scope.Variables)
                InsertDebugVariable(scopeItem, variable);
            SendMessageW(_variables, TVM_EXPAND, (nint)TVE_EXPAND, scopeItem);
        }

        // the preview keeps showing the same variable from pause to pause
        FishboneDebugVariable? followed = frame.Scopes.SelectMany(scope => scope.Variables)
            .FirstOrDefault(variable => variable.Name == _previewName && variable.ImageHandle is not null);
        if (followed is null)
            ClearPreview();
        else
            ShowPreview(new VariableNode { Name = followed.Name, IsImage = true, Debug = followed });
    }

    // after a run without the debugger, the script's own variables. functions registered by the
    // host are left out
    private static void ShowFinalVariables(FishboneEnvironment environment, FishboneConfiguration? configuration)
    {
        ClearVariables();
        _finalConfiguration = configuration;
        foreach (var (name, value) in environment.Values)
            if (value is not Delegate)
                InsertLocalValue(TVI_ROOT, name, value);

        if (_previewName is not null && environment.Values.TryGetValue(_previewName, out object? shown)
            && configuration?.CanVisualize(shown) == true)
            ShowPreview(new VariableNode { Name = _previewName, IsImage = true, Value = shown });
        else
            ClearPreview();
    }

    private static void InsertDebugVariable(IntPtr parent, FishboneDebugVariable variable)
    {
        bool isImage = variable.ImageHandle is not null;
        InsertItem(parent, ItemText(variable.Name, variable.Value, variable.Type, isImage),
            hasChildren: variable.ChildrenHandle is not null, new VariableNode { Name = variable.Name, IsImage = isImage, Debug = variable });
    }

    private static void InsertLocalValue(IntPtr parent, string name, object? value)
    {
        bool isImage = _finalConfiguration?.CanVisualize(value) == true;
        InsertItem(parent, ItemText(name, DebugValueFormatter.FormatValue(value), DebugValueFormatter.FormatType(value), isImage),
            hasChildren: value is IDictionary or (IEnumerable and not string),
            new VariableNode { Name = name, IsImage = isImage, Value = value });
    }

    // the type is left out when the value already says it, like an object shown by its type name
    private static string ItemText(string name, string value, string? type, bool isImage)
    {
        string text = string.IsNullOrEmpty(type) || type == value ? $"{name} = {value}" : $"{name} = {value}   ({type})";
        return isImage ? text + "   [image]" : text;
    }

    private static IntPtr InsertItem(IntPtr parent, string text, bool hasChildren, VariableNode? node, bool bold = false)
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
                    mask = TVIF_TEXT | TVIF_PARAM | TVIF_CHILDREN | TVIF_STATE,
                    state = bold ? TVIS_BOLD : 0,
                    stateMask = TVIS_BOLD,
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

    // selecting an image previews it, double-clicking opens it in its own window, and children
    // load the first time an item opens
    private static void OnVariablesNotification(IntPtr lParam)
    {
        int code = Marshal.PtrToStructure<NMHDR>(lParam).code;
        if (code == NM_DBLCLK)
        {
            if (_selectedNode is { IsImage: true } selected)
                OpenImage(selected);
            return;
        }
        var notification = Marshal.PtrToStructure<NMTREEVIEWW>(lParam);
        if (code == TVN_SELCHANGEDW)
        {
            _selectedNode = _nodes.GetValueOrDefault(notification.itemNew.lParam);
            if (_selectedNode is { IsImage: true } chosen)
                ShowPreview(chosen);
            return;
        }
        if (code != TVN_ITEMEXPANDINGW || notification.action != TVE_EXPAND)
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
