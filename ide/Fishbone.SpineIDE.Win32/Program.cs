// --------------------------------------------------------------------------------
// Program.cs
//
// SpineIDE for Windows: a raw win32 front end with a scintilla editor and the output
// on the left, and an image preview over the variables on the right. the gaps between
// them can be dragged. running and debugging go through the shared ScriptSession.
// --------------------------------------------------------------------------------

using SpineIDE.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
        WS_VSCROLL = 0x00200000, WS_HSCROLL = 0x00100000, WS_BORDER = 0x00800000, SS_CENTERIMAGE = 0x200, SBARS_SIZEGRIP = 0x100;
    private const uint ES_MULTILINE = 0x4, ES_AUTOVSCROLL = 0x40, ES_AUTOHSCROLL = 0x80, ES_READONLY = 0x800;
    private const uint WM_DESTROY = 0x2, WM_SIZE = 0x5, WM_CLOSE = 0x10, WM_SETCURSOR = 0x20, WM_SETFONT = 0x30, WM_NOTIFY = 0x4E, WM_CAPTURECHANGED = 0x215,
        WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_COMMAND = 0x111, WM_TIMER = 0x113, WM_CTLCOLORSTATIC = 0x138, WM_DPICHANGED = 0x2E0, WM_APP_INVOKE = 0x8001;
    private const uint EM_SETSEL = 0xB1, EM_REPLACESEL = 0xC2, EM_SETLIMITTEXT = 0xC5, EM_SETCUEBANNER = 0x1501;
    private const int VK_RETURN = 0x0D, VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_F5 = 0x74, VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A,
        VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_N = 0x4E, VK_O = 0x4F, VK_S = 0x53;
    private const int RunButtonId = 1, DebugButtonId = 2, TopBarHeight = 32, InputHeight = 24, HeaderHeight = 22, Gap = 6;

    // the delegate has to stay referenced or the gc collects it while windows still calls it
    private static readonly WndProcDelegate _wndProc = WndProc;

    private static IntPtr _window, _editor, _output, _input, _variables, _status;
    private static IntPtr _outputHeader, _variablesHeader, _uncheckAllButton, _fullScreenButton;
    private static readonly List<IntPtr> _toolbar = [];

    // run on the left. the debugger's buttons on the right, where debug turns into continue
    // while a debug session is on
    private static readonly (int Id, string Label)[] ToolbarCommands =
    [
        (RunButtonId, "Run"),
        (DebugButtonId, "Debug"), (CommandPause, "Pause"), (CommandStop, "Stop"),
        (CommandStepOver, "Step Over"), (CommandStepInto, "Step Into"), (CommandStepOut, "Step Out"),
    ];

    // only the buttons that do something right now are enabled
    private static void UpdateToolbar()
    {
        for (int i = 0; i < _toolbar.Count; i++)
            EnableWindow(_toolbar[i], ToolbarCommands[i].Id switch
            {
                RunButtonId => !_running,
                DebugButtonId => !_running || _paused,
                CommandPause => _running && !_paused,
                CommandStop => _running,
                _ => _paused,
            });
        SetDebugButton();
    }

    // lets background threads (the debug client) run code on the ui thread
    private static readonly ConcurrentQueue<Action> _uiQueue = new();

    [STAThread]
    private static int Main(string[] args)
    {
        // no console here, so a bad command line shows in a message box
        var errors = new StringWriter();
        if (!SpineIdeStartupOptions.TryParse(args, errors, out SpineIdeStartupOptions options))
        {
            MessageBoxW(IntPtr.Zero, errors.ToString(), "SpineIDE", 0x10); // MB_ICONERROR
            return 1;
        }

        SetupSession();

        IntPtr instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x3, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            // the sdk embeds ApplicationIcon under this id
            hIcon = LoadIconW(instance, 32512),
            hCursor = LoadCursorW(IntPtr.Zero, 32512), // IDC_ARROW
            hbrBackground = 16, // COLOR_BTNFACE + 1
            lpszClassName = "SpineIDE",
        };
        RegisterClassExW(ref windowClass);

        // open on the monitor under the mouse, so the window gets that monitor's dpi from the start
        GetCursorPos(out POINT cursor);
        var monitor = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfoW(MonitorFromPoint(cursor, 2), ref monitor); // MONITOR_DEFAULTTONEAREST
        RECT work = monitor.rcWork;
        _window = CreateWindowExW(0, "SpineIDE", "SpineIDE", WS_OVERLAPPEDWINDOW,
            work.left, work.top, 1100, 800, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        SetMenu(_window, CreateMainMenu());

        // mica title bar on windows 11 22h2+, older versions ignore the attribute
        int mica = 2; // DWMSBT_MAINWINDOW
        DwmSetWindowAttribute(_window, 38, ref mica, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
        // the "accent color on title bars" setting paints over mica, so ask for no caption color
        int noColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_window, 35, ref noColor, sizeof(int)); // DWMWA_CAPTION_COLOR

        // loading the dll registers the "Scintilla" window class
        NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "Scintilla.dll"));
        _editor = CreateChild("Scintilla", "", WS_CHILD | WS_VISIBLE | WS_BORDER, 0);
        SetupEditor();
        SetupWatchInput();

        uint editStyle = WS_CHILD | WS_VISIBLE | WS_VSCROLL | WS_HSCROLL | WS_BORDER
            | ES_MULTILINE | ES_AUTOVSCROLL | ES_AUTOHSCROLL | ES_READONLY;
        _output = CreateChild("EDIT", "", editStyle, 0);
        // the line input() reads from. it's only enabled while a script waits on it
        _input = CreateChild("EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL, 0);
        SendMessageW(_input, EM_SETCUEBANNER, 1, "input() waits here. Enter sends the line");
        EnableWindow(_input, false);
        // ICC_TREEVIEW_CLASSES registers the tree view class
        // ICC_BAR_CLASSES registers the status bar
        var controls = new INITCOMMONCONTROLSEX { dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = 0x2 | 0x4 };
        InitCommonControlsEx(ref controls);
        _variables = CreateChild("SysTreeView32", "",
            WS_CHILD | WS_VISIBLE | WS_BORDER | TVS_HASBUTTONS | TVS_LINESATROOT | TVS_SHOWSELALWAYS | TVS_FULLROWSELECT, 0);
        // the explorer look: arrows instead of plus boxes, and a full-row hover highlight
        SetWindowTheme(_variables, "Explorer", null);
        SendMessageW(_variables, TVM_SETEXTENDEDSTYLE, (nint)TVS_EX_DOUBLEBUFFER, (nint)TVS_EX_DOUBLEBUFFER);
        // checkboxes have to be turned on after the tree is made, before it has items
        SetWindowLongPtrW(_variables, -16, GetWindowLongPtrW(_variables, -16) | (nint)TVS_CHECKBOXES); // GWL_STYLE

        RegisterImageClass();
        _preview = CreateWindowExW(0, "SpineIDE.Image", "", WS_CHILD | WS_VISIBLE | WS_BORDER,
            0, 0, 0, 0, _window, IntPtr.Zero, instance, IntPtr.Zero);
        foreach (var (id, label) in ToolbarCommands)
            _toolbar.Add(CreateChild("BUTTON", label, WS_CHILD | WS_VISIBLE, id));
        UpdateToolbar();
        // a status bar keeps its own place at the bottom, and WM_SETTEXT sets its text
        _status = CreateChild("msctls_statusbar32", "", WS_CHILD | WS_VISIBLE | SBARS_SIZEGRIP, 0);
        _outputHeader = CreateChild("STATIC", "Output", WS_CHILD | WS_VISIBLE | SS_CENTERIMAGE, 0);
        _previewHeader = CreateChild("STATIC", "Image", WS_CHILD | WS_VISIBLE | SS_CENTERIMAGE, 0);
        _variablesHeader = CreateChild("STATIC", "Variables", WS_CHILD | WS_VISIBLE | SS_CENTERIMAGE, 0);
        _fullScreenButton = CreateChild("BUTTON", "Full screen", WS_CHILD | WS_VISIBLE, CommandFullScreen);
        _uncheckAllButton = CreateChild("BUTTON", "Uncheck all", WS_CHILD | WS_VISIBLE, CommandUncheckAll);

        // multiline edits cap at 32k chars by default, 0 lifts that
        SendMessageW(_output, EM_SETLIMITTEXT, 0, 0);
        _dpi = (int)GetDpiForWindow(_window);
        ApplyDpi();

        // centered in the work area, which leaves out the taskbar
        int width = Math.Min(Scale(1100), work.right - work.left);
        int height = Math.Min(Scale(800), work.bottom - work.top);
        SetWindowPos(_window, IntPtr.Zero, work.left + (work.right - work.left - width) / 2,
            work.top + (work.bottom - work.top - height) / 2, width, height, 0x14); // SWP_NOZORDER | SWP_NOACTIVATE

        if (options.FilePath is not null)
            LoadDocument(File.ReadAllText(options.FilePath), Path.GetFullPath(options.FilePath));
        else
            LoadDocument("", null);

        GetClientRect(_window, out RECT client);
        Layout(client.right, client.bottom);
        ShowWindow(_window, 1);
        UpdateWindow(_window);

        double startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        SetWindowTextW(_status, $"ready in {startupMs:F0} ms");

        // loading plugins for completion touches disk, so do it before the first keystroke needs it
        _ = Task.Run(() => _ = SpineConfiguration.Description);

        // a host started us to debug its script, like RunDebuggableAsync does
        if (options.AttachPort is int port)
        {
            Attach(port);
            ListenForReattach();
        }

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_KEYDOWN && msg.hwnd == _input && (int)msg.wParam == VK_RETURN)
            {
                SubmitInput();
                continue;
            }
            // Enter adds the watch, unless it picks from the completion list
            if (msg.message == WM_KEYDOWN && msg.hwnd == _watchInput && (int)msg.wParam == VK_RETURN && WatchSci(SCI_AUTOCACTIVE) == 0)
            {
                AddWatch();
                continue;
            }
            // F10 arrives as a system key
            if ((msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN) && HandleKey((int)msg.wParam))
                continue;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
        return 0;
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_SIZE:
                Layout((int)(lParam & 0xFFFF), (int)((lParam >> 16) & 0xFFFF));
                return 0;
            // the gaps between the panes are the window's own, so the mouse reaches it there
            case WM_SETCURSOR when wParam == hwnd && (lParam & 0xFFFF) == 1 && SplitterUnderCursor() is var splitter and not Splitter.None: // HTCLIENT
                SetCursor(LoadCursorW(IntPtr.Zero, splitter == Splitter.Columns ? 32644 : 32645)); // IDC_SIZEWE : IDC_SIZENS
                return 1;
            case WM_LBUTTONDOWN:
                StartDrag((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                return 0;
            case WM_MOUSEMOVE when _dragging != Splitter.None:
                Drag((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
                return 0;
            case WM_LBUTTONUP when _dragging != Splitter.None:
                ReleaseCapture();
                return 0;
            case WM_CAPTURECHANGED:
                _dragging = Splitter.None;
                return 0;
            case WM_DPICHANGED:
                // moved to a monitor with another scale. windows suggests the new window rect
                _dpi = (int)(wParam & 0xFFFF);
                RECT suggested = Marshal.PtrToStructure<RECT>(lParam);
                SetWindowPos(hwnd, IntPtr.Zero, suggested.left, suggested.top,
                    suggested.right - suggested.left, suggested.bottom - suggested.top, 0x14); // SWP_NOZORDER | SWP_NOACTIVATE
                ApplyDpi();
                return 0;
            case WM_COMMAND:
                RunCommand((int)(wParam & 0xFFFF));
                return 0;
            case WM_TIMER when wParam == AnalysisTimer:
                AnalyzeScript();
                return 0;
            // a read-only edit paints gray by default. the output reads better on white, like the editor
            case WM_CTLCOLORSTATIC when lParam == _output:
                SetBkColor(wParam, GetSysColor(5)); // COLOR_WINDOW
                return GetSysColorBrush(5);
            case WM_CLOSE:
                if (ConfirmDiscard())
                {
                    _ = _session.StopAsync();
                    DestroyWindow(hwnd);
                }
                return 0;
            case WM_NOTIFY:
                IntPtr from = Marshal.PtrToStructure<NMHDR>(lParam).hwndFrom;
                if (from == _editor)
                    OnEditorNotification(Marshal.PtrToStructure<SCNotification>(lParam));
                else if (from == _watchInput)
                    OnWatchInputNotification(Marshal.PtrToStructure<SCNotification>(lParam));
                else if (from == _variables)
                    OnVariablesNotification(lParam);
                return 0;
            case WM_APP_INVOKE:
                while (_uiQueue.TryDequeue(out Action? action))
                    action();
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static bool HandleKey(int key)
    {
        bool ctrl = GetKeyState(VK_CONTROL) < 0;
        bool shift = GetKeyState(VK_SHIFT) < 0;
        int? command = key switch
        {
            VK_F5 when ctrl => RunButtonId,
            VK_F5 when shift => CommandStop,
            VK_F5 => DebugButtonId,
            VK_F9 => CommandBreakpoint,
            VK_F10 => CommandStepOver,
            VK_F11 when shift => CommandStepOut,
            VK_F11 => CommandStepInto,
            VK_N when ctrl => CommandNew,
            VK_O when ctrl => CommandOpen,
            VK_S when ctrl && shift => CommandSaveAs,
            VK_S when ctrl => CommandSave,
            _ => null
        };
        if (command is not null)
        {
            RunCommand(command.Value);
            return true;
        }
        if (key == VK_SPACE && ctrl)
        {
            if (GetFocus() == _watchInput)
                ShowWatchCompletion(forced: true);
            else
                ShowCompletion(forced: true);
            return true;
        }
        // scintilla still gets the key and closes its popup. Escape on the completion list leaves the tip
        if (key == VK_ESCAPE && SendMessageW(GetFocus() == _watchInput ? _watchInput : _editor, SCI_AUTOCACTIVE, 0, 0) == 0)
            _tipDismissed = true;
        return false;
    }

    private static void Post(Action action)
    {
        _uiQueue.Enqueue(action);
        PostMessageW(_window, WM_APP_INVOKE, 0, 0);
    }

    private static IntPtr CreateChild(string className, string text, uint style, int id) =>
        CreateWindowExW(0, className, text, style, 0, 0, 0, 0, _window, id, IntPtr.Zero, IntPtr.Zero);

    // the monitor's dpi, 96 at 100% scale. sizes in this file are written for 96
    private static int _dpi = 96;
    private static IntPtr _monoFont, _guiFont, _headerFont, _treeFont;

    private static int Scale(int pixels) => pixels * _dpi / 96;

    // fonts and fixed sizes, for the current monitor
    private static void ApplyDpi()
    {
        foreach (IntPtr font in new[] { _monoFont, _guiFont, _headerFont, _treeFont })
            DeleteObject(font);
        _monoFont = CreateFontW(-Scale(16), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Consolas");
        _guiFont = CreateFontW(-Scale(12), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        _headerFont = CreateFontW(-Scale(12), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        _treeFont = CreateFontW(-Scale(14), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        foreach (IntPtr control in new[] { _output, _input })
            SendMessageW(control, WM_SETFONT, _monoFont, 1);
        foreach (IntPtr control in _toolbar.Append(_status).Append(_fullScreenButton).Append(_uncheckAllButton))
            SendMessageW(control, WM_SETFONT, _guiFont, 1);
        foreach (IntPtr control in new[] { _outputHeader, _previewHeader, _variablesHeader })
            SendMessageW(control, WM_SETFONT, _headerFont, 1);
        SendMessageW(_variables, WM_SETFONT, _treeFont, 1);
        SendMessageW(_variables, TVM_SETITEMHEIGHT, Scale(24), 0);
        SetToolbarIcons();
        SetWatchInputDpi();
        SetMarginWidths();

        GetClientRect(_window, out RECT client);
        Layout(client.right, client.bottom);
    }

    // the editor over the output on the left, the image over the variables on the right. the
    // splits are kept as parts of the space they divide, so resizing the window keeps them
    private static double _rightSplit = 0.35, _editorSplit = 0.62, _imageSplit = 0.5;

    private enum Splitter { None, Columns, Editor, Image }

    // each gap, with the header under it, which drags too. the sizes are what dragging changes,
    // out of the room each one has
    private static RECT _columnsGap, _editorGap, _imageGap;
    private static int _rightWidth, _editorHeight, _imageHeight, _columnsRoom, _editorRoom, _imageRoom;
    private static Splitter _dragging;
    private static int _dragFrom, _dragSize;

    private static Splitter SplitterAt(int x, int y)
    {
        static bool Inside(RECT rect, int x, int y) => x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
        return Inside(_columnsGap, x, y) ? Splitter.Columns
            : Inside(_editorGap, x, y) ? Splitter.Editor
            : Inside(_imageGap, x, y) ? Splitter.Image
            : Splitter.None;
    }

    private static Splitter SplitterUnderCursor()
    {
        GetCursorPos(out POINT cursor);
        ScreenToClient(_window, ref cursor);
        return SplitterAt(cursor.x, cursor.y);
    }

    private static void StartDrag(int x, int y)
    {
        _dragging = SplitterAt(x, y);
        if (_dragging == Splitter.None)
            return;
        (_dragFrom, _dragSize) = _dragging switch
        {
            Splitter.Columns => (x, _rightWidth),
            Splitter.Editor => (y, _editorHeight),
            _ => (y, _imageHeight),
        };
        SetCapture(_window);
    }

    // the right column grows leftward, the editor and the image downward
    private static void Drag(int x, int y)
    {
        switch (_dragging)
        {
            case Splitter.Columns: _rightSplit = (double)(_dragSize - (x - _dragFrom)) / _columnsRoom; break;
            case Splitter.Editor: _editorSplit = (double)(_dragSize + y - _dragFrom) / _editorRoom; break;
            case Splitter.Image: _imageSplit = (double)(_dragSize + y - _dragFrom) / _imageRoom; break;
        }
        GetClientRect(_window, out RECT client);
        Layout(client.right, client.bottom);
    }

    // value, kept between low and high. when there's no room for both, low wins
    private static int Between(int value, int low, int high) => Math.Max(low, Math.Min(value, high));

    private static void Layout(int width, int height)
    {
        int top = Scale(TopBarHeight), input = Scale(InputHeight), header = Scale(HeaderHeight), gap = Scale(Gap);
        int indent = Scale(6);
        // run on the left, and the debugger's buttons laid out from the right end. the window
        // sizes itself before the buttons exist
        int buttonWidth = Scale(88);
        if (_toolbar.Count > 0)
            MoveWindow(_toolbar[0], Scale(8), Scale(4), buttonWidth, Scale(24), true);
        int x = width - Scale(8);
        for (int i = _toolbar.Count - 1; i >= 1; i--)
        {
            x -= buttonWidth;
            MoveWindow(_toolbar[i], x, Scale(4), buttonWidth, Scale(24), true);
            // a wider gap between the session and the stepping buttons
            x -= Scale(i == 4 ? 16 : 4);
        }

        // the status bar sizes itself on WM_SIZE, the panes take the rest
        SendMessageW(_status, WM_SIZE, 0, 0);
        GetWindowRect(_status, out RECT statusRect);
        height -= statusRect.bottom - statusRect.top;

        // every pane keeps some room, so a splitter can't hide one
        int least = Scale(80);
        _columnsRoom = width - gap;
        int rightWidth = _rightWidth = Between((int)(_columnsRoom * _rightSplit), least, _columnsRoom - least);
        int leftWidth = width - rightWidth - gap;
        _editorRoom = height - top - gap - header - input;
        int editorHeight = _editorHeight = Between((int)(_editorRoom * _editorSplit), least, _editorRoom - least);
        MoveWindow(_editor, 0, top, leftWidth, editorHeight, true);
        int outputTop = top + editorHeight + gap;
        MoveWindow(_outputHeader, indent, outputTop, leftWidth - indent, header, true);
        MoveWindow(_output, 0, outputTop + header, leftWidth, height - outputTop - header - input, true);
        MoveWindow(_input, 0, height - input, leftWidth, input, true);

        int rightX = leftWidth + gap;
        int button = Scale(80);
        _imageRoom = height - top - 2 * header - gap;
        int imageHeight = _imageHeight = Between((int)(_imageRoom * _imageSplit), least, _imageRoom - least);
        MoveWindow(_previewHeader, rightX + indent, top, rightWidth - indent - button, header, true);
        MoveWindow(_fullScreenButton, width - button, top + Scale(1), button, header - Scale(2), true);
        MoveWindow(_preview, rightX, top + header, rightWidth, imageHeight, true);
        int variablesTop = top + header + imageHeight + gap;
        MoveWindow(_variablesHeader, rightX + indent, variablesTop, rightWidth - indent - button, header, true);
        MoveWindow(_uncheckAllButton, width - button, variablesTop + Scale(1), button, header - Scale(2), true);
        // the watch line, then the tree
        int label = Scale(48), watchTop = variablesTop + header;
        MoveWindow(_watchLabel, rightX + indent, watchTop, label - indent, input, true);
        MoveWindow(_watchInput, rightX + label, watchTop, rightWidth - label, input, true);
        MoveWindow(_variables, rightX, watchTop + input + Scale(2), rightWidth, height - watchTop - input - Scale(2), true);

        _columnsGap = new RECT { left = leftWidth, top = top, right = rightX, bottom = height };
        _editorGap = new RECT { left = 0, top = top + editorHeight, right = leftWidth, bottom = outputTop + header };
        _imageGap = new RECT { left = rightX, top = top + header + imageHeight, right = width - button, bottom = variablesTop + header };
    }

}
