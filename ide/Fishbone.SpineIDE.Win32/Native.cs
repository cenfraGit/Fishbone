using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    // --------------------------------------------------------------------------------
    // win32 interop
    // --------------------------------------------------------------------------------

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    // the leading fields of scintilla's SCNotification, up to the ones used here
    [StructLayout(LayoutKind.Sequential)]
    private struct SCNotification
    {
        public IntPtr hwndFrom, idFrom;
        public int code;
        public IntPtr position;
        public int ch, modifiers, modificationType;
        public IntPtr text, length, linesAdded;
        public int message;
        public IntPtr wParam, lParam, line;
        public int foldLevelNow, foldLevelPrev, margin, listType, x, y, token;
        public IntPtr annotationLinesAdded;
        public int updated, listCompletionMethod, characterSource;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TVITEMW
    {
        public uint mask;
        public IntPtr hItem;
        public uint state, stateMask;
        // a raw pointer: in notifications it's only set when the mask says so
        public IntPtr pszText;
        public int cchTextMax, iImage, iSelectedImage, cChildren;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TVINSERTSTRUCTW
    {
        public IntPtr hParent, hInsertAfter;
        public TVITEMW item;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMHDR
    {
        public IntPtr hwndFrom, idFrom;
        public int code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NMTREEVIEWW
    {
        public NMHDR hdr;
        public uint action;
        public TVITEMW itemOld, itemNew;
        public int ptDragX, ptDragY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INITCOMMONCONTROLSEX
    {
        public uint dwSize, dwICC;
    }

    [DllImport("comctl32.dll")]
    private static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX controls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, ref TVINSERTSTRUCTW lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        // pairs of description and pattern, separated by nul characters
        public string lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter, nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir, lpstrTitle;
        public int Flags;
        public short nFileOffset, nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData, lpfnHook;
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved, FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW dialog);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateMenu();

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, IntPtr idOrSubmenu, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenu(IntPtr hwnd, IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hwnd, bool enable);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEX windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG msg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, byte[] lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowTextW(IntPtr hwnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint elapseMs, IntPtr callback);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr hwnd, IntPtr id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("gdi32.dll")]
    private static extern uint SetBkColor(IntPtr dc, uint color);

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSysColorBrush(int index);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string appName, string? idList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
