using Fishbone;
using Fishbone.DebugClient;
using Fishbone.Debugging;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WS_EX_TOOLWINDOW = 0x80, WM_MOUSEMOVE = 0x200,
        WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_LBUTTONDBLCLK = 0x203, WM_MOUSEWHEEL = 0x20A;
    private const uint WS_POPUP = 0x80000000, WS_CLIPCHILDREN = 0x02000000;
    private const int CW_USEDEFAULT = unchecked((int)0x80000000);
    private const int NM_DBLCLK = -3;

    // the delegate has to stay referenced, like the main window's
    private static readonly WndProcDelegate _imageWndProc = ImageWndProc;
    private static readonly Dictionary<IntPtr, ImageView> _images = [];

    // one shown image and how it's zoomed. Scale is screen pixels per image pixel, and X, Y is
    // where the image's top left corner sits in the window. Width and Height are the image's own
    // size, which the shapes are in. the bitmap can be smaller, since big images are sent scaled
    // down, and it's 0 for shapes alone, which sit on a black canvas
    private sealed class ImageView
    {
        public required IntPtr Bitmap;
        public required int BitmapWidth, BitmapHeight, Width, Height;
        public required string Name, Title;
        public required List<ShapeLayer> Layers;
        public bool Fit = true;
        public double Scale = 1, X, Y;
        public int DragX, DragY;
        public bool Dragging;
    }

    // one variable's shapes, drawn over the image in one color
    private sealed record ShapeLayer(uint Color, IReadOnlyList<FishboneDebugRegion> Regions, IReadOnlyList<FishboneDebugContour> Contours);

    // a variable's loaded image, by name
    private sealed record StackEntry(string Name, FishboneDebugImage Image);

    // red, green, blue, yellow, cyan, magenta, like HDevelop's colors. ARGB
    private static readonly uint[] LayerColors = [0xFFFF0000, 0xFF00FF00, 0xFF0000FF, 0xFFFFFF00, 0xFF00FFFF, 0xFFFF00FF];

    private static IntPtr _gdiplusToken;

    // the docked preview shows the checked variables: the image, with the shapes stacked over it
    // in the order they were checked. it follows them by name, so stepping keeps showing the same
    // ones. the full screen window, while it's open, shows the same
    private static IntPtr _preview, _previewHeader, _fullScreen, _fullScreenHeader;
    private static readonly List<string> _previewNames = [];
    // the checked variables' images, for the current pause or run
    private static readonly Dictionary<string, FishboneDebugImage> _previewImages = [];
    private static List<StackEntry> _previewStack = [];
    // bumped when the variables change, so an image that finishes loading late is dropped
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
    private static async Task<FishboneDebugImage?> LoadImage(VariableNode node)
    {
        if (node.Debug is not null)
            return node.Debug.ImageHandle is { } handle && _pausedSession is { } session
                ? await session.GetImageAsync(handle)
                : null;
        FishboneConfiguration? configuration = _finalConfiguration;
        object? value = node.Value;
        return await Task.Run(() => configuration?.Visualize(value) is { } image ? ToDebugImage(image) : null);
    }

    private static FishboneDebugImage ToDebugImage(FishboneImage image) => new(
        image.HasPixels ? image.ToPng() : [], image.Width, image.Height,
        image.Regions.Select(region => new FishboneDebugRegion(region.Rows, region.ColumnStarts, region.ColumnEnds)).ToArray(),
        image.Contours.Select(contour => new FishboneDebugContour(contour.Rows, contour.Columns)).ToArray());

    private static bool IsOnlyShapes(FishboneDebugImage image) => image.Png.Length == 0;

    // checking a variable stacks it over the preview, and unchecking takes it out. one image
    // shows at a time, so checking an image unchecks the one shown before
    private static async void CheckPreview(VariableNode node, bool isChecked)
    {
        string name = node.Name;
        _previewNames.Remove(name);
        _previewImages.Remove(name);
        if (!isChecked)
        {
            UpdatePreview();
            return;
        }
        _previewNames.Add(name);

        int version = _previewVersion;
        try
        {
            FishboneDebugImage? image = await LoadImage(node);
            Post(() =>
            {
                if (version != _previewVersion || image is null || !_previewNames.Contains(name))
                    return;
                if (!IsOnlyShapes(image))
                    foreach (string other in _previewImages.Where(entry => !IsOnlyShapes(entry.Value)).Select(entry => entry.Key).ToList())
                    {
                        _previewNames.Remove(other);
                        _previewImages.Remove(other);
                        SetChecked(other, false);
                    }
                _previewImages[name] = image;
                UpdatePreview();
            });
        }
        catch (Exception exception)
        {
            Post(() => SetWindowTextW(_status, $"{name} couldn't be shown: {exception.Message}"));
        }
    }

    // after a pause or a run, loads the checked variables again. one that's gone stays checked,
    // and shows again once it's back
    private static async void FollowPreview(Func<string, VariableNode?> find)
    {
        int version = ++_previewVersion;
        _previewImages.Clear();
        List<VariableNode> nodes = _previewNames.Select(find).OfType<VariableNode>().ToList();
        var images = new Dictionary<string, FishboneDebugImage>();
        foreach (VariableNode node in nodes)
        {
            try
            {
                if (await LoadImage(node) is { } image)
                    images[node.Name] = image;
            }
            catch (Exception exception)
            {
                Post(() => SetWindowTextW(_status, $"{node.Name} couldn't be shown: {exception.Message}"));
            }
        }
        Post(() =>
        {
            if (version != _previewVersion)
                return;
            foreach (var (name, image) in images)
                if (_previewNames.Contains(name))
                    _previewImages[name] = image;
            UpdatePreview();
        });
    }

    // the checked ones in the order they were checked, with the image moved under the shapes.
    // two images can be checked when one of them was gone at the time, and then the last one shows
    private static void UpdatePreview()
    {
        List<StackEntry> stack = _previewNames.Where(_previewImages.ContainsKey)
            .Select(name => new StackEntry(name, _previewImages[name])).ToList();
        if (stack.FindLast(entry => !IsOnlyShapes(entry.Image)) is { } image)
            stack = [image, .. stack.Where(entry => IsOnlyShapes(entry.Image))];
        SetPreview(stack);
    }

    private static void SetPreview(List<StackEntry> stack)
    {
        _previewStack = stack;
        ImageView? view = ShowStack(_preview, stack);
        SetWindowTextW(_previewHeader, view?.Title ?? (stack.Count > 0 ? "Image: couldn't be decoded" : "Image"));
        if (_fullScreen != 0)
            ShowStack(_fullScreen, stack);
    }

    // puts the stack in an image window. the same image as before keeps its zoom, so stepping
    // and checking shapes don't move it
    private static ImageView? ShowStack(IntPtr hwnd, List<StackEntry> stack)
    {
        ImageView? view = stack.Count > 0 ? CreateView(stack) : null;
        if (_images.Remove(hwnd, out ImageView? previous))
            DeleteObject(previous.Bitmap);
        if (view is not null)
        {
            view.Title = hwnd == _fullScreen ? view.Title + "   (Escape closes)" : "Image: " + view.Title;
            if (previous is not null && previous.Name == view.Name && previous.Width == view.Width && previous.Height == view.Height)
                (view.Fit, view.Scale, view.X, view.Y) = (previous.Fit, previous.Scale, previous.X, previous.Y);
            _images[hwnd] = view;
        }
        if (hwnd == _fullScreen)
            SetWindowTextW(_fullScreenHeader, view?.Title ?? "nothing checked   (Escape closes)");
        InvalidateRect(hwnd, IntPtr.Zero, true);
        return view;
    }

    // keeps _previewNames, so the variables show again once they have an image
    private static void ClearPreview()
    {
        _previewVersion++;
        _previewImages.Clear();
        SetPreview([]);
    }

    private static void UncheckAll()
    {
        foreach (string name in _previewNames)
            SetChecked(name, false);
        _previewNames.Clear();
        ClearPreview();
    }

    // the preview over the whole monitor, following it like the docked one
    private static void ShowFullScreen()
    {
        if (_fullScreen == 0)
        {
            var monitor = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfoW(MonitorFromWindow(_window, 2), ref monitor); // MONITOR_DEFAULTTONEAREST
            RECT area = monitor.rcMonitor;
            // clip the header, so painting the image doesn't cover it
            _fullScreen = CreateWindowExW(0, "SpineIDE.Image", "SpineIDE", WS_POPUP | WS_CLIPCHILDREN, area.left, area.top,
                area.right - area.left, area.bottom - area.top, _window, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
            _fullScreenHeader = CreateWindowExW(0, "STATIC", "", WS_CHILD | WS_VISIBLE | SS_CENTERIMAGE, Scale(6), 0,
                area.right - area.left - Scale(6), Scale(HeaderHeight), _fullScreen, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            SendMessageW(_fullScreenHeader, WM_SETFONT, _headerFont, 1);
            ShowStack(_fullScreen, _previewStack);
        }
        ShowWindow(_fullScreen, 1);
        SetFocus(_fullScreen);
    }

    // the first entry is the image, and every entry with shapes adds a layer in the next color.
    // null when the image can't be decoded
    private static ImageView? CreateView(List<StackEntry> stack)
    {
        FishboneDebugImage first = stack[0].Image;
        var (bitmap, bitmapWidth, bitmapHeight) = IsOnlyShapes(first) ? (0, 0, 0) : DecodePng(first.Png);
        if (!IsOnlyShapes(first) && bitmap == 0)
            return null;

        // an adapter that doesn't send the size sends the png at full size
        int width = first.Width > 0 ? first.Width : bitmapWidth, height = first.Height > 0 ? first.Height : bitmapHeight;
        var layers = new List<ShapeLayer>();
        foreach (StackEntry entry in stack)
        {
            if (entry.Image.Regions.Count == 0 && entry.Image.Contours.Count == 0)
                continue;
            layers.Add(new ShapeLayer(LayerColors[layers.Count % LayerColors.Length], entry.Image.Regions, entry.Image.Contours));
            // shapes alone sit on a canvas big enough for all of them
            if (bitmap == 0)
                (width, height) = (Math.Max(width, entry.Image.Width), Math.Max(height, entry.Image.Height));
        }
        (width, height) = (Math.Max(1, width), Math.Max(1, height));

        string names = string.Join(" + ", stack.Select(entry => entry.Name));
        return new ImageView
        {
            Bitmap = bitmap, BitmapWidth = bitmapWidth, BitmapHeight = bitmapHeight, Width = width, Height = height,
            Name = stack[0].Name, Title = $"{names} ({width}x{height})", Layers = layers,
        };
    }

    // double-clicking an image variable also opens it in its own, bigger window. one that's in
    // the preview opens with everything stacked there
    private static async void OpenImage(VariableNode node)
    {
        try
        {
            List<StackEntry> stack = _previewStack.Any(entry => entry.Name == node.Name)
                ? _previewStack
                : await LoadImage(node) is { } image ? [new StackEntry(node.Name, image)] : [];
            if (stack.Count > 0)
                Post(() => OpenImageWindow(stack));
        }
        catch (Exception exception)
        {
            Post(() => SetWindowTextW(_status, $"the image couldn't be shown: {exception.Message}"));
        }
    }

    private static void OpenImageWindow(List<StackEntry> stack)
    {
        ImageView? view = CreateView(stack);
        if (view is null)
        {
            SetWindowTextW(_status, "the image couldn't be decoded");
            return;
        }

        // show it at its own size, up to most of a typical screen
        var frame = new RECT { right = Math.Min(view.Width, 1400), bottom = Math.Min(view.Height, 900) };
        AdjustWindowRectEx(ref frame, WS_OVERLAPPEDWINDOW, false, WS_EX_TOOLWINDOW);
        IntPtr window = CreateWindowExW(WS_EX_TOOLWINDOW, "SpineIDE.Image", view.Title, WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT, CW_USEDEFAULT, frame.right - frame.left, frame.bottom - frame.top, _window, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        _images[window] = view;
        ShowWindow(window, 1);
    }

    // the GDI+ flat api decodes PNG with plain calls, no COM wrappers
    private static (IntPtr Bitmap, int Width, int Height) DecodePng(byte[] png)
    {
        StartGdiplus();
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
            case WM_PAINT when hwnd == _preview || hwnd == _fullScreen:
                PaintPlaceholder(hwnd);
                return 0;
            case WM_KEYDOWN when hwnd == _fullScreen && (int)wParam == VK_ESCAPE:
                DestroyWindow(hwnd);
                return 0;
            // the image paints its own background, so dragging doesn't flicker
            case WM_ERASEBKGND when _images.ContainsKey(hwnd):
                return 1;
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
                SetWindowTextW(HeaderOf(hwnd), view.Title);
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return 0;
            case WM_DESTROY:
                if (hwnd == _fullScreen)
                    _fullScreen = 0;
                if (_images.Remove(hwnd, out var gone))
                    DeleteObject(gone.Bitmap);
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
        SetWindowTextW(HeaderOf(hwnd), $"{view.Title}   {view.Scale * 100:F0}%{pointer}");
    }

    // where an image window shows its title: the preview and the full screen one have a header,
    // and a pop-out its title bar
    private static IntPtr HeaderOf(IntPtr hwnd) =>
        hwnd == _preview ? _previewHeader : hwnd == _fullScreen ? _fullScreenHeader : hwnd;

    private static void PaintPlaceholder(IntPtr hwnd)
    {
        IntPtr dc = BeginPaint(hwnd, out PAINTSTRUCT paint);
        GetClientRect(hwnd, out RECT client);
        SetBkMode(dc, 1); // TRANSPARENT
        SetTextColor(dc, 0xE0E0E0);
        IntPtr previous = SelectObject(dc, _guiFont);
        DrawTextW(dc, "check an image variable to preview it", -1, ref client, 0x25); // DT_CENTER | DT_VCENTER | DT_SINGLELINE
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
            // the whole image, centered, keeping its proportions. full screen, it fits under the header
            int top = hwnd == _fullScreen ? Scale(HeaderHeight) : 0;
            view.Scale = Math.Min((double)client.right / view.Width, (double)(client.bottom - top) / view.Height);
            view.X = (client.right - view.Width * view.Scale) / 2;
            view.Y = top + (client.bottom - top - view.Height * view.Scale) / 2;
        }

        IntPtr buffer = CreateCompatibleDC(dc);
        IntPtr bufferBitmap = CreateCompatibleBitmap(dc, client.right, client.bottom);
        IntPtr previousBuffer = SelectObject(buffer, bufferBitmap);
        // COLOR_APPWORKSPACE, like the class background. full screen, a dark gray that's easier on the eyes
        FillRect(buffer, ref client, hwnd == _fullScreen ? GetStockObject(3) : GetSysColorBrush(12)); // DKGRAY_BRUSH

        var target = new RECT
        {
            left = (int)Math.Round(view.X), top = (int)Math.Round(view.Y),
            right = (int)Math.Round(view.X + view.Width * view.Scale), bottom = (int)Math.Round(view.Y + view.Height * view.Scale),
        };
        if (view.Bitmap == 0)
        {
            FillRect(buffer, ref target, GetStockObject(4)); // BLACK_BRUSH
        }
        else
        {
            IntPtr source = CreateCompatibleDC(dc);
            IntPtr previous = SelectObject(source, view.Bitmap);
            // smooth when shrinking, sharp pixels when enlarging
            SetStretchBltMode(buffer, target.right - target.left < view.BitmapWidth ? 4 : 3); // HALFTONE : COLORONCOLOR
            SetBrushOrgEx(buffer, 0, 0, IntPtr.Zero);
            StretchBlt(buffer, target.left, target.top, target.right - target.left, target.bottom - target.top,
                source, 0, 0, view.BitmapWidth, view.BitmapHeight, 0x00CC0020); // SRCCOPY
            SelectObject(source, previous);
            DeleteDC(source);
        }
        if (view.Layers.Count > 0)
            PaintShapes(buffer, view, client);

        BitBlt(dc, 0, 0, client.right, client.bottom, buffer, 0, 0, 0x00CC0020);
        SelectObject(buffer, previousBuffer);
        DeleteObject(bufferBitmap);
        DeleteDC(buffer);
        EndPaint(hwnd, ref paint);
    }

    private static void StartGdiplus()
    {
        if (_gdiplusToken != 0)
            return;
        var input = new GdiplusStartupInput { GdiplusVersion = 1 };
        GdiplusStartup(out _gdiplusToken, ref input, IntPtr.Zero);
    }

    // regions fill see-through, so the image shows under them, and contours are lines. pixel
    // (row, column) covers column to column + 1 on screen, and a contour point at a whole row and
    // column sits on that pixel's center, like HALCON places them
    private static void PaintShapes(IntPtr dc, ImageView view, RECT client)
    {
        StartGdiplus();
        GdipCreateFromHDC(dc, out IntPtr graphics);
        try
        {
            GdipSetPixelOffsetMode(graphics, 4); // PixelOffsetModeHalf, so pixel x spans x to x + 1 like in gdi
            foreach (ShapeLayer layer in view.Layers)
            {
                GdipSetSmoothingMode(graphics, 3); // SmoothingModeNone, so run edges stay sharp
                GdipCreateSolidFill(layer.Color & 0x00FFFFFF | 0x80000000, out IntPtr brush);
                foreach (FishboneDebugRegion region in layer.Regions)
                {
                    GpRectF[] rects = RegionRects(region, view, client);
                    if (rects.Length > 0)
                        GdipFillRectangles(graphics, brush, rects, rects.Length);
                }
                GdipDeleteBrush(brush);

                GdipSetSmoothingMode(graphics, 4); // SmoothingModeAntiAlias
                GdipCreatePen1(layer.Color, 1, 2, out IntPtr pen); // UnitPixel
                foreach (FishboneDebugContour contour in layer.Contours)
                {
                    if (contour.Rows.Length < 2)
                        continue;
                    var points = new GpPointF[contour.Rows.Length];
                    for (int i = 0; i < points.Length; i++)
                        points[i] = new GpPointF((float)(view.X + (contour.Columns[i] + 0.5) * view.Scale),
                            (float)(view.Y + (contour.Rows[i] + 0.5) * view.Scale));
                    GdipDrawLines(graphics, pen, points, points.Length);
                }
                GdipDeletePen(pen);
            }
        }
        finally
        {
            GdipDeleteGraphics(graphics);
        }
    }

    // the runs that are on screen. zoomed out, several rows land on one screen row, so there the
    // runs are merged per screen row, or the see-through fill would darken where they overlap
    private static GpRectF[] RegionRects(FishboneDebugRegion region, ImageView view, RECT client)
    {
        double scale = view.Scale;
        var rects = new List<GpRectF>();
        if (scale >= 1)
        {
            for (int i = 0; i < region.Rows.Length; i++)
            {
                double top = view.Y + region.Rows[i] * scale;
                double left = view.X + region.ColumnStarts[i] * scale, right = view.X + (region.ColumnEnds[i] + 1) * scale;
                if (top + scale >= 0 && top <= client.bottom && right >= 0 && left <= client.right)
                    rects.Add(new GpRectF((float)left, (float)top, (float)(right - left), (float)scale));
            }
            return [.. rects];
        }

        var spans = new Dictionary<int, List<(int Start, int End)>>();
        for (int i = 0; i < region.Rows.Length; i++)
        {
            int y = (int)Math.Floor(view.Y + region.Rows[i] * scale);
            int start = (int)Math.Floor(view.X + region.ColumnStarts[i] * scale);
            int end = Math.Max(start + 1, (int)Math.Ceiling(view.X + (region.ColumnEnds[i] + 1) * scale));
            if (y < 0 || y >= client.bottom || end <= 0 || start >= client.right)
                continue;
            if (!spans.TryGetValue(y, out var row))
                spans[y] = row = [];
            row.Add((Math.Max(0, start), Math.Min(client.right, end)));
        }
        foreach (var (y, row) in spans)
        {
            row.Sort();
            var (start, end) = row[0];
            foreach (var span in row.Skip(1))
            {
                if (span.Start <= end)
                {
                    end = Math.Max(end, span.End);
                    continue;
                }
                rects.Add(new GpRectF(start, y, end - start, 1));
                (start, end) = span;
            }
            rects.Add(new GpRectF(start, y, end - start, 1));
        }
        return [.. rects];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpRectF(float x, float y, float width, float height)
    {
        public float X = x, Y = y, Width = width, Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpPointF(float x, float y)
    {
        public float X = x, Y = y;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateFromHDC(IntPtr dc, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteGraphics(IntPtr graphics);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSetPixelOffsetMode(IntPtr graphics, int mode);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateSolidFill(uint argb, out IntPtr brush);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeleteBrush(IntPtr brush);

    [DllImport("gdiplus.dll")]
    private static extern int GdipFillRectangles(IntPtr graphics, IntPtr brush, GpRectF[] rects, int count);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreatePen1(uint argb, float width, int unit, out IntPtr pen);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDeletePen(IntPtr pen);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawLines(IntPtr graphics, IntPtr pen, GpPointF[] points, int count);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

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
}
