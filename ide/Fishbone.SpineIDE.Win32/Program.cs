// --------------------------------------------------------------------------------
// Program.cs
//
// SpineIDE for Windows: a raw win32 front end with a scintilla editor on top, and
// output and variables below. running and debugging go through the shared
// ScriptSession, like the Avalonia SpineIDE.
// --------------------------------------------------------------------------------

using Antlr4.Runtime;
using Fishbone;
using Fishbone.DebugClient;
using SpineIDE.Services;
using SpineIDE.Views.Editor;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
        WS_VSCROLL = 0x00200000, WS_HSCROLL = 0x00100000, WS_BORDER = 0x00800000;
    private const uint ES_MULTILINE = 0x4, ES_AUTOVSCROLL = 0x40, ES_AUTOHSCROLL = 0x80, ES_READONLY = 0x800;
    private const uint WM_DESTROY = 0x2, WM_SIZE = 0x5, WM_CLOSE = 0x10, WM_SETFONT = 0x30, WM_NOTIFY = 0x4E,
        WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104, WM_COMMAND = 0x111, WM_APP_INVOKE = 0x8001;
    private const uint EM_SETSEL = 0xB1, EM_REPLACESEL = 0xC2, EM_SETLIMITTEXT = 0xC5, EM_SETCUEBANNER = 0x1501;
    private const int VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_F5 = 0x74, VK_F9 = 0x78, VK_F10 = 0x79, VK_F11 = 0x7A,
        VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_N = 0x4E, VK_O = 0x4F, VK_S = 0x53;
    private const int RunButtonId = 1, DebugButtonId = 2, TopBarHeight = 32, InputHeight = 24;

    private const string DefaultScript = """
        // F5 debug / continue    Ctrl+F5 run    Shift+F5 stop
        // F9 breakpoint    F10 step over    F11 step into    Shift+F11 step out
        // Ctrl+Space completion
        func square(x)
        {
            return x * x;
        }

        let i = 0;
        while (i < 10000)
        {
            let squared = square(i);
            println("line " + i.ToString() + " squared is " + squared.ToString());
            i = i + 1;
        }
        """;

    // the delegate has to stay referenced or the gc collects it while windows still calls it
    private static readonly WndProcDelegate _wndProc = WndProc;

    private static IntPtr _window, _editor, _output, _input, _variables, _runButton, _debugButton, _status;

    // lets background threads (the debug client) run code on the ui thread
    private static readonly ConcurrentQueue<Action> _uiQueue = new();

    [STAThread]
    private static int Main(string[] args)
    {
        SetupSession();

        IntPtr instance = GetModuleHandleW(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0x3, // CS_HREDRAW | CS_VREDRAW
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            hCursor = LoadCursorW(IntPtr.Zero, 32512), // IDC_ARROW
            hbrBackground = 16, // COLOR_BTNFACE + 1
            lpszClassName = "SpineIDE",
        };
        RegisterClassExW(ref windowClass);

        _window = CreateWindowExW(0, "SpineIDE", "SpineIDE", WS_OVERLAPPEDWINDOW,
            100, 100, 1100, 800, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
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

        uint editStyle = WS_CHILD | WS_VISIBLE | WS_VSCROLL | WS_HSCROLL | WS_BORDER
            | ES_MULTILINE | ES_AUTOVSCROLL | ES_AUTOHSCROLL | ES_READONLY;
        _output = CreateChild("EDIT", "", editStyle, 0);
        // the line input() reads from. it's only enabled while a script waits on it
        _input = CreateChild("EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_AUTOHSCROLL, 0);
        SendMessageW(_input, EM_SETCUEBANNER, 1, "input() waits here. Enter sends the line");
        EnableWindow(_input, false);
        // ICC_TREEVIEW_CLASSES registers the tree view class
        var controls = new INITCOMMONCONTROLSEX { dwSize = (uint)Marshal.SizeOf<INITCOMMONCONTROLSEX>(), dwICC = 0x2 };
        InitCommonControlsEx(ref controls);
        _variables = CreateChild("SysTreeView32", "",
            WS_CHILD | WS_VISIBLE | WS_BORDER | TVS_HASBUTTONS | TVS_HASLINES | TVS_LINESATROOT | TVS_SHOWSELALWAYS, 0);
        _runButton = CreateChild("BUTTON", "Run (Ctrl+F5)", WS_CHILD | WS_VISIBLE, RunButtonId);
        _debugButton = CreateChild("BUTTON", "Debug (F5)", WS_CHILD | WS_VISIBLE, DebugButtonId);
        _status = CreateChild("STATIC", "", WS_CHILD | WS_VISIBLE, 0);

        IntPtr mono = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Consolas");
        IntPtr gui = CreateFontW(-12, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
        SendMessageW(_output, WM_SETFONT, mono, 1);
        SendMessageW(_input, WM_SETFONT, mono, 1);
        // multiline edits cap at 32k chars by default, 0 lifts that
        SendMessageW(_output, EM_SETLIMITTEXT, 0, 0);
        SendMessageW(_variables, WM_SETFONT, mono, 1);
        foreach (IntPtr control in new[] { _runButton, _debugButton, _status })
            SendMessageW(control, WM_SETFONT, gui, 1);

        if (args.Length > 0)
            LoadDocument(File.ReadAllText(args[0]), Path.GetFullPath(args[0]));
        else
            LoadDocument(DefaultScript, null);

        GetClientRect(_window, out RECT client);
        Layout(client.right, client.bottom);
        ShowWindow(_window, 1);
        UpdateWindow(_window);

        double startupMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        SetWindowTextW(_status, $"ready in {startupMs:F0} ms");

        // loading plugins for completion touches disk, so do it before the first keystroke needs it
        _ = Task.Run(() => _ = FishboneCompletionCatalog.Shared);

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_KEYDOWN && msg.hwnd == _input && (int)msg.wParam == VK_RETURN)
            {
                SubmitInput();
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
            case WM_COMMAND:
                RunCommand((int)(wParam & 0xFFFF));
                return 0;
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
            ShowCompletion(forced: true);
            return true;
        }
        return false;
    }

    private static void Post(Action action)
    {
        _uiQueue.Enqueue(action);
        PostMessageW(_window, WM_APP_INVOKE, 0, 0);
    }

    private static IntPtr CreateChild(string className, string text, uint style, int id) =>
        CreateWindowExW(0, className, text, style, 0, 0, 0, 0, _window, id, IntPtr.Zero, IntPtr.Zero);

    private static void Layout(int width, int height)
    {
        int editorHeight = (height - TopBarHeight) * 6 / 10;
        int bottomHeight = height - TopBarHeight - editorHeight;
        int outputWidth = width * 6 / 10;
        MoveWindow(_runButton, 8, 4, 110, 24, true);
        MoveWindow(_debugButton, 124, 4, 110, 24, true);
        MoveWindow(_status, 246, 9, width - 254, 20, true);
        MoveWindow(_editor, 0, TopBarHeight, width, editorHeight, true);
        MoveWindow(_output, 0, TopBarHeight + editorHeight, outputWidth, bottomHeight - InputHeight, true);
        MoveWindow(_input, 0, TopBarHeight + editorHeight + bottomHeight - InputHeight, outputWidth, InputHeight, true);
        MoveWindow(_variables, outputWidth, TopBarHeight + editorHeight, width - outputWidth, bottomHeight, true);
    }

}
