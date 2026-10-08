using Fishbone.DebugClient;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint WM_PAINT = 0xF, WS_EX_TOOLWINDOW = 0x80;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int NM_DBLCLK = -3;
    private const uint TVM_GETNEXTITEM = 0x110A, TVM_GETITEMW = 0x113E, TVGN_CARET = 0x9;

    // the delegate has to stay referenced, like the main window's
    private static readonly WndProcDelegate _imageWndProc = ImageWndProc;
    private static readonly Dictionary<IntPtr, (IntPtr Bitmap, int Width, int Height)> _images = [];
    private static IntPtr _gdiplusToken;

    // double-clicking an image variable opens it in its own window
    private static void OnVariableDoubleClick()
    {
        IntPtr selected = SendMessageW(_variables, TVM_GETNEXTITEM, (nint)TVGN_CARET, 0);
        var item = new TVITEMW { mask = TVIF_PARAM, hItem = selected };
        SendMessageW(_variables, TVM_GETITEMW, 0, ref item);
        if (_nodes.TryGetValue(item.lParam, out VariableNode? node)
            && node.Debug is { ImageHandle: { } handle } variable
            && _pausedSession is { } session)
            ShowImage(variable.Name, session, handle);
    }

    private static async void ShowImage(string name, IFishboneDebugClientSession session, FishboneVariableHandle handle)
    {
        try
        {
            byte[] png = await session.GetImageAsync(handle);
            Post(() => OpenImageWindow(name, png));
        }
        catch (Exception exception)
        {
            Post(() => SetWindowTextW(_status, $"the image couldn't be shown: {exception.Message}"));
        }
    }

    private static void OpenImageWindow(string name, byte[] png)
    {
        if (_gdiplusToken == 0)
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero);

            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0x3, // CS_HREDRAW | CS_VREDRAW
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_imageWndProc),
                hInstance = GetModuleHandleW(null),
                hCursor = LoadCursorW(IntPtr.Zero, 32512),
                hbrBackground = 13, // COLOR_GRAYTEXT + 1, so the image's edges stand out
                lpszClassName = "SpineIDE.Image",
            };
            RegisterClassExW(ref windowClass);
        }

        var (bitmap, width, height) = DecodePng(png);
        if (bitmap == 0)
        {
            SetWindowTextW(_status, "the image couldn't be decoded");
            return;
        }

        // show it at its own size, up to most of a typical screen
        var frame = new RECT { right = Math.Min(width, 1400), bottom = Math.Min(height, 900) };
        AdjustWindowRectEx(ref frame, WS_OVERLAPPEDWINDOW, false, WS_EX_TOOLWINDOW);
        IntPtr window = CreateWindowExW(WS_EX_TOOLWINDOW, "SpineIDE.Image", $"{name} ({width}x{height})", WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, frame.right - frame.left, frame.bottom - frame.top, _window, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        _images[window] = (bitmap, width, height);
        ShowWindow(window, 1);
    }

    // the GDI+ flat api decodes PNG with plain calls, no COM wrappers
    private static (IntPtr Bitmap, int Width, int Height) DecodePng(byte[] png)
    {
        IntPtr stream = SHCreateMemStream(png, (uint)png.Length);
        if (stream == 0)
            return (0, 0, 0);
        try
        {
            if (GdipCreateBitmapFromStream(stream, out IntPtr image) != 0)
                return (0, 0, 0);
            try
            {
                GdipGetImageWidth(image, out uint width);
                GdipGetImageHeight(image, out uint height);
                GdipCreateHBITMAPFromBitmap(image, out IntPtr bitmap, unchecked((int)0xFFFFFFFF));
                return (bitmap, (int)width, (int)height);
            }
            finally
            {
                GdipDisposeImage(image);
            }
        }
        finally
        {
            Marshal.Release(stream);
        }
    }

    private static IntPtr ImageWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_PAINT when _images.TryGetValue(hwnd, out var image):
                PaintImage(hwnd, image.Bitmap, image.Width, image.Height);
                return 0;
            case WM_SIZE:
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return 0;
            case WM_DESTROY when _images.Remove(hwnd, out var image):
                DeleteObject(image.Bitmap);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // fits the image in the window, keeping its proportions
    private static void PaintImage(IntPtr hwnd, IntPtr bitmap, int width, int height)
    {
        IntPtr dc = BeginPaint(hwnd, out PAINTSTRUCT paint);
        GetClientRect(hwnd, out RECT client);
        double scale = Math.Min((double)client.right / width, (double)client.bottom / height);
        int drawWidth = (int)(width * scale), drawHeight = (int)(height * scale);
        int x = (client.right - drawWidth) / 2, y = (client.bottom - drawHeight) / 2;

        IntPtr source = CreateCompatibleDC(dc);
        IntPtr previous = SelectObject(source, bitmap);
        // smooth when shrinking, sharp pixels when enlarging
        SetStretchBltMode(dc, scale < 1 ? 4 : 3); // HALFTONE : COLORONCOLOR
        SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
        StretchBlt(dc, x, y, drawWidth, drawHeight, source, 0, 0, width, height, 0x00CC0020); // SRCCOPY
        SelectObject(source, previous);
        DeleteDC(source);
        EndPaint(hwnd, ref paint);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateBitmapFromStream(IntPtr stream, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateHBITMAPFromBitmap(IntPtr bitmap, out IntPtr hbitmap, int background);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageWidth(IntPtr image, out uint width);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageHeight(IntPtr image, out uint height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(IntPtr image);

    [DllImport("shlwapi.dll")]
    private static extern IntPtr SHCreateMemStream(byte[] data, uint size);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, uint operation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, ref TVITEMW lParam);
}
