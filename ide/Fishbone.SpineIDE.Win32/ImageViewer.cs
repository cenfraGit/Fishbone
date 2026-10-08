using Fishbone;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WS_EX_TOOLWINDOW = 0x80, WM_MOUSEMOVE = 0x200,
        WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_MOUSEWHEEL = 0x20A;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int NM_DBLCLK = -3;
    private const uint TVM_GETNEXTITEM = 0x110A, TVM_GETITEMW = 0x113E, TVGN_CARET = 0x9;

    // the delegate has to stay referenced, like the main window's
    private static readonly WndProcDelegate _imageWndProc = ImageWndProc;
    private static readonly Dictionary<IntPtr, ImageView> _images = [];

    // one shown image and how it's zoomed. Scale is screen pixels per image pixel, and X, Y is
    // where the image's top left corner sits in the window
    private sealed class ImageView
    {
        public required IntPtr Bitmap;
        public required int Width, Height;
        public required string Name, Title;
        public bool Fit = true;
        public double Scale = 1, X, Y;
        public int DragX, DragY;
        public bool Dragging;
    }
    private static IntPtr _gdiplusToken;

    // the docked preview. it follows a variable by name, so stepping keeps showing the same one
    private static IntPtr _preview, _previewHeader;
    private static string? _previewName;
    // bumped on every change, so an image that finishes loading late is dropped
    private static int _previewVersion;

    // the docked preview and the pop-out windows share one window class
    private static void RegisterImageClass()
    {
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0xB, // CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_imageWndProc),
            hInstance = GetModuleHandleW(null),
            hCursor = LoadCursorW(IntPtr.Zero, 32512),
            hbrBackground = 13, // COLOR_APPWORKSPACE + 1, so the image's edges stand out
            lpszClassName = "SpineIDE.Image",
        };
        RegisterClassExW(ref windowClass);
    }

    // a paused image comes from the debug host. a final value is rendered here, with the run's visualizers
    private static async Task<byte[]?> LoadPng(VariableNode node)
    {
        if (node.Debug is not null)
            return node.Debug.ImageHandle is { } handle && _pausedSession is { } session
                ? await session.GetImageAsync(handle)
                : null;
        FishboneConfiguration? configuration = _finalConfiguration;
        object? value = node.Value;
        return await Task.Run(() => configuration?.Visualize(value)?.ToPng());
    }

    private static async void ShowPreview(VariableNode node)
    {
        _previewName = node.Name;
        int version = ++_previewVersion;
        try
        {
            byte[]? png = await LoadPng(node);
            Post(() =>
            {
                if (version == _previewVersion)
                    SetPreview(node.Name, png);
            });
        }
        catch (Exception exception)
        {
            Post(() =>
            {
                if (version != _previewVersion)
                    return;
                ClearPreview();
                SetWindowTextW(_previewHeader, $"Image: {node.Name} couldn't be shown ({exception.Message})");
            });
        }
    }

    private static void SetPreview(string name, byte[]? png)
    {
        ImageView? previous = _images.GetValueOrDefault(_preview);
        ClearPreview();
        var (bitmap, width, height) = png is null ? (0, 0, 0) : DecodePng(png);
        if (bitmap == 0)
        {
            SetWindowTextW(_previewHeader, $"Image: {name} has nothing to show");
            return;
        }
        var view = new ImageView { Bitmap = bitmap, Width = width, Height = height, Name = name, Title = $"Image: {name} ({width}x{height})" };
        // stepping refreshes the same image, so it keeps its zoom
        if (previous is not null && previous.Name == name && previous.Width == width && previous.Height == height)
            (view.Fit, view.Scale, view.X, view.Y) = (previous.Fit, previous.Scale, previous.X, previous.Y);
        _images[_preview] = view;
        SetWindowTextW(_previewHeader, view.Title);
        InvalidateRect(_preview, IntPtr.Zero, true);
    }

    // keeps _previewName, so the variable shows again once it has an image
    private static void ClearPreview()
    {
        _previewVersion++;
        if (_images.Remove(_preview, out var view))
            DeleteObject(view.Bitmap);
        SetWindowTextW(_previewHeader, "Image");
        InvalidateRect(_preview, IntPtr.Zero, true);
    }

    private static VariableNode? SelectedNode()
    {
        IntPtr selected = SendMessageW(_variables, TVM_GETNEXTITEM, (nint)TVGN_CARET, 0);
        var item = new TVITEMW { mask = TVIF_PARAM, hItem = selected };
        SendMessageW(_variables, TVM_GETITEMW, 0, ref item);
        return _nodes.GetValueOrDefault(item.lParam);
    }

    // double-clicking an image variable also opens it in its own, bigger window
    private static async void OpenImage(VariableNode node)
    {
        try
        {
            byte[]? png = await LoadPng(node);
            if (png is not null)
                Post(() => OpenImageWindow(node.Name, png));
        }
        catch (Exception exception)
        {
            Post(() => SetWindowTextW(_status, $"the image couldn't be shown: {exception.Message}"));
        }
    }

    private static void OpenImageWindow(string name, byte[] png)
    {
        var (bitmap, width, height) = DecodePng(png);
        if (bitmap == 0)
        {
            SetWindowTextW(_status, "the image couldn't be decoded");
            return;
        }

        // show it at its own size, up to most of a typical screen
        var frame = new RECT { right = Math.Min(width, 1400), bottom = Math.Min(height, 900) };
        AdjustWindowRectEx(ref frame, WS_OVERLAPPEDWINDOW, false, WS_EX_TOOLWINDOW);
        string title = $"{name} ({width}x{height})";
        IntPtr window = CreateWindowExW(WS_EX_TOOLWINDOW, "SpineIDE.Image", title, WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, frame.right - frame.left, frame.bottom - frame.top, _window, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        _images[window] = new ImageView { Bitmap = bitmap, Width = width, Height = height, Name = name, Title = title };
        ShowWindow(window, 1);
    }

    // the GDI+ flat api decodes PNG with plain calls, no COM wrappers
    private static (IntPtr Bitmap, int Width, int Height) DecodePng(byte[] png)
    {
        if (_gdiplusToken == 0)
        {
            var input = new GdiplusStartupInput { GdiplusVersion = 1 };
            GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero);
        }

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

    // the wheel zooms around the cursor, dragging moves the image, and a double-click fits it again
    private static IntPtr ImageWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        int mouseX = (short)(lParam & 0xFFFF), mouseY = (short)((lParam >> 16) & 0xFFFF);
        switch (msg)
        {
            case WM_PAINT when _images.TryGetValue(hwnd, out var view):
                PaintImage(hwnd, view);
                return 0;
            case WM_PAINT when hwnd == _preview:
                PaintPlaceholder(hwnd);
                return 0;
            // the image paints its own background, so dragging doesn't flicker
            case WM_ERASEBKGND when _images.ContainsKey(hwnd):
                return 1;
            case WM_SIZE:
                InvalidateRect(hwnd, IntPtr.Zero, true);
                return 0;
            case WM_MOUSEWHEEL when _images.TryGetValue(hwnd, out var view):
            {
                // the wheel reports screen coordinates
                var point = new POINT { x = mouseX, y = mouseY };
                ScreenToClient(hwnd, ref point);
                double factor = (short)((wParam >> 16) & 0xFFFF) > 0 ? 1.25 : 0.8;
                double scale = Math.Clamp(view.Scale * factor, 0.01, 64);
                // keep the pixel under the cursor where it is
                view.X = point.x - (point.x - view.X) * scale / view.Scale;
                view.Y = point.y - (point.y - view.Y) * scale / view.Scale;
                view.Scale = scale;
                view.Fit = false;
                ShowPointer(hwnd, view, point.x, point.y);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return 0;
            }
            case WM_LBUTTONDOWN when _images.TryGetValue(hwnd, out var view):
                // the wheel goes to the focused window
                SetFocus(hwnd);
                SetCapture(hwnd);
                (view.Dragging, view.DragX, view.DragY) = (true, mouseX, mouseY);
                return 0;
            case WM_MOUSEMOVE when _images.TryGetValue(hwnd, out var view):
                if (view.Dragging)
                {
                    view.X += mouseX - view.DragX;
                    view.Y += mouseY - view.DragY;
                    (view.DragX, view.DragY, view.Fit) = (mouseX, mouseY, false);
                    InvalidateRect(hwnd, IntPtr.Zero, false);
                }
                ShowPointer(hwnd, view, mouseX, mouseY);
                return 0;
            case WM_LBUTTONUP when _images.TryGetValue(hwnd, out var view):
                view.Dragging = false;
                ReleaseCapture();
                return 0;
            case WM_LBUTTONDBLCLK when _images.TryGetValue(hwnd, out var view):
                view.Fit = true;
                SetWindowTextW(hwnd == _preview ? _previewHeader : hwnd, view.Title);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return 0;
            case WM_DESTROY when _images.Remove(hwnd, out var view):
                DeleteObject(view.Bitmap);
                return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // the pixel under the cursor and the zoom, in the preview's header or the pop-out's title.
    // row and column, like HALCON counts them
    private static void ShowPointer(IntPtr hwnd, ImageView view, int x, int y)
    {
        int column = (int)Math.Floor((x - view.X) / view.Scale), row = (int)Math.Floor((y - view.Y) / view.Scale);
        string pointer = column >= 0 && row >= 0 && column < view.Width && row < view.Height ? $"   row {row}, col {column}" : "";
        SetWindowTextW(hwnd == _preview ? _previewHeader : hwnd, $"{view.Title}   {view.Scale * 100:F0}%{pointer}");
    }

    private static void PaintPlaceholder(IntPtr hwnd)
    {
        IntPtr dc = BeginPaint(hwnd, out PAINTSTRUCT paint);
        GetClientRect(hwnd, out RECT client);
        SetBkMode(dc, 1); // TRANSPARENT
        SetTextColor(dc, 0xE0E0E0);
        IntPtr previous = SelectObject(dc, _guiFont);
        DrawTextW(dc, "select an image variable to preview it", -1, ref client, 0x25); // DT_CENTER | DT_VCENTER | DT_SINGLELINE
        SelectObject(dc, previous);
        EndPaint(hwnd, ref paint);
    }

    // draws into a buffer first, so moving the image doesn't flicker
    private static void PaintImage(IntPtr hwnd, ImageView view)
    {
        IntPtr dc = BeginPaint(hwnd, out PAINTSTRUCT paint);
        GetClientRect(hwnd, out RECT client);
        if (view.Fit)
        {
            // the whole image, centered, keeping its proportions
            view.Scale = Math.Min((double)client.right / view.Width, (double)client.bottom / view.Height);
            view.X = (client.right - view.Width * view.Scale) / 2;
            view.Y = (client.bottom - view.Height * view.Scale) / 2;
        }

        IntPtr buffer = CreateCompatibleDC(dc);
        IntPtr bufferBitmap = CreateCompatibleBitmap(dc, client.right, client.bottom);
        IntPtr previousBuffer = SelectObject(buffer, bufferBitmap);
        FillRect(buffer, ref client, GetSysColorBrush(12)); // COLOR_APPWORKSPACE, like the class background

        IntPtr source = CreateCompatibleDC(dc);
        IntPtr previous = SelectObject(source, view.Bitmap);
        // smooth when shrinking, sharp pixels when enlarging
        SetStretchBltMode(buffer, view.Scale < 1 ? 4 : 3); // HALFTONE : COLORONCOLOR
        SetBrushOrgEx(buffer, 0, 0, IntPtr.Zero);
        StretchBlt(buffer, (int)Math.Round(view.X), (int)Math.Round(view.Y), (int)Math.Round(view.Width * view.Scale),
            (int)Math.Round(view.Height * view.Scale), source, 0, 0, view.Width, view.Height, 0x00CC0020); // SRCCOPY
        SelectObject(source, previous);
        DeleteDC(source);

        BitBlt(dc, 0, 0, client.right, client.bottom, buffer, 0, 0, 0x00CC0020);
        SelectObject(buffer, previousBuffer);
        DeleteObject(bufferBitmap);
        DeleteDC(buffer);
        EndPaint(hwnd, ref paint);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x, y;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint rop);

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
    private static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr dc, uint color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr dc, string text, int length, ref RECT rect, uint format);

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
