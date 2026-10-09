using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint BCM_SETIMAGELIST = 0x1602;
    // ARGB
    private const uint IconGreen = 0xFF2E9E44, IconBlue = 0xFF1F6FD0, IconRed = 0xFFD13438, IconGray = 0xFFAAAAAA;

    // one image list per button, by command. the debug button swaps between two of them
    private static readonly Dictionary<int, IntPtr> _toolbarIcons = [];
    private static bool? _debugButtonContinues;

    // the icons are drawn for the monitor's dpi, so this runs again when it changes
    private static void SetToolbarIcons()
    {
        List<IntPtr> previous = [.. _toolbarIcons.Values];
        foreach (int command in ToolbarCommands.Select(command => command.Id).Append(CommandContinue))
            _toolbarIcons[command] = IconList(command, Scale(16));
        for (int i = 0; i < _toolbar.Count; i++)
            if (ToolbarCommands[i].Id != DebugButtonId)
                SetButtonIcons(_toolbar[i], _toolbarIcons[ToolbarCommands[i].Id]);
        _debugButtonContinues = null;
        SetDebugButton();
        foreach (IntPtr list in previous)
            ImageList_Destroy(list);
    }

    // debug starts a session, and while one is on the same button continues it
    private static void SetDebugButton()
    {
        if (_debugButtonContinues == _debugging || _toolbarIcons.Count == 0)
            return;
        _debugButtonContinues = _debugging;
        IntPtr button = _toolbar[Array.FindIndex(ToolbarCommands, command => command.Id == DebugButtonId)];
        SetWindowTextW(button, _debugging ? "Continue" : "Debug");
        SetButtonIcons(button, _toolbarIcons[_debugging ? CommandContinue : DebugButtonId]);
    }

    // one image per button state: normal, hot, pressed, disabled, default and stylus hot
    private static IntPtr IconList(int command, int size)
    {
        var (draw, color) = ToolbarIcon(command);
        IntPtr normal = DrawIcon(size, draw, color), disabled = DrawIcon(size, draw, IconGray);
        IntPtr list = ImageList_Create(size, size, 0x20, 6, 0); // ILC_COLOR32
        foreach (IntPtr bitmap in new[] { normal, normal, normal, disabled, normal, normal })
            ImageList_Add(list, bitmap, IntPtr.Zero);
        DeleteObject(normal);
        DeleteObject(disabled);
        return list;
    }

    private static void SetButtonIcons(IntPtr button, IntPtr list)
    {
        var image = new BUTTON_IMAGELIST { himl = list, margin = new RECT { left = Scale(2) } };
        SendMessageW(button, BCM_SETIMAGELIST, 0, ref image);
        InvalidateRect(button, IntPtr.Zero, true);
    }

    // drawn on a 16 by 16 grid: triangles to run, bars to pause, a square to stop, and arrows
    // over, into and out of a dot to step
    private static (Action<IntPtr, uint> Draw, uint Color) ToolbarIcon(int command) => command switch
    {
        RunButtonId => ((g, c) => StrokePolygon(g, c, 4, 2.5f, 13, 8, 4, 13.5f), IconGreen),
        DebugButtonId => ((g, c) => FillPolygon(g, c, 3.5f, 2, 13.5f, 8, 3.5f, 14), IconGreen),
        CommandContinue => ((g, c) =>
        {
            FillRectangle(g, c, 2.5f, 2.5f, 2, 11);
            FillPolygon(g, c, 6, 2.5f, 14, 8, 6, 13.5f);
        }, IconGreen),
        CommandPause => ((g, c) =>
        {
            FillRectangle(g, c, 3.5f, 2.5f, 3, 11);
            FillRectangle(g, c, 9.5f, 2.5f, 3, 11);
        }, IconBlue),
        CommandStop => ((g, c) => FillRectangle(g, c, 3, 3, 10, 10), IconRed),
        CommandStepOver => ((g, c) =>
        {
            WithPen(g, c, pen => GdipDrawArc(g, pen, 2, 3, 10, 10, 180, 180));
            FillPolygon(g, c, 9.5f, 7.5f, 14.5f, 7.5f, 12, 10.5f);
            FillDot(g, c, 7, 13);
        }, IconBlue),
        CommandStepInto => ((g, c) =>
        {
            WithPen(g, c, pen => GdipDrawLine(g, pen, 8, 1.5f, 8, 8));
            FillPolygon(g, c, 4.5f, 7, 11.5f, 7, 8, 10.5f);
            FillDot(g, c, 8, 13.5f);
        }, IconBlue),
        _ => ((g, c) =>
        {
            WithPen(g, c, pen => GdipDrawLine(g, pen, 8, 10, 8, 4.5f));
            FillPolygon(g, c, 4.5f, 5, 11.5f, 5, 8, 1.5f);
            FillDot(g, c, 8, 13.5f);
        }, IconBlue),
    };

    // a transparent bitmap of the icon, scaled from the 16 by 16 grid
    private static IntPtr DrawIcon(int size, Action<IntPtr, uint> draw, uint color)
    {
        StartGdiplus();
        GdipCreateBitmapFromScan0(size, size, 0, 0x26200A, IntPtr.Zero, out IntPtr bitmap); // PixelFormat32bppARGB
        GdipGetImageGraphicsContext(bitmap, out IntPtr graphics);
        GdipGraphicsClear(graphics, 0);
        GdipSetSmoothingMode(graphics, 4); // SmoothingModeAntiAlias
        GdipScaleWorldTransform(graphics, size / 16f, size / 16f, 0);
        draw(graphics, color);
        GdipDeleteGraphics(graphics);
        GdipCreateHBITMAPFromBitmap(bitmap, out IntPtr result, 0);
        GdipDisposeImage(bitmap);
        return result;
    }

    private static GpPointF[] Points(float[] xy) =>
        Enumerable.Range(0, xy.Length / 2).Select(i => new GpPointF(xy[2 * i], xy[2 * i + 1])).ToArray();

    private static void WithBrush(uint color, Action<IntPtr> use)
    {
        GdipCreateSolidFill(color, out IntPtr brush);
        use(brush);
        GdipDeleteBrush(brush);
    }

    // 1.6 units wide, with round joins so corners don't spike
    private static void WithPen(IntPtr graphics, uint color, Action<IntPtr> use)
    {
        GdipCreatePen1(color, 1.6f, 0, out IntPtr pen); // UnitWorld
        GdipSetPenLineJoin(pen, 2); // LineJoinRound
        use(pen);
        GdipDeletePen(pen);
    }

    private static void FillPolygon(IntPtr graphics, uint color, params float[] xy)
    {
        GpPointF[] points = Points(xy);
        WithBrush(color, brush => GdipFillPolygon(graphics, brush, points, points.Length, 0));
    }

    private static void StrokePolygon(IntPtr graphics, uint color, params float[] xy)
    {
        GpPointF[] points = Points(xy);
        WithPen(graphics, color, pen => GdipDrawPolygon(graphics, pen, points, points.Length));
    }

    private static void FillRectangle(IntPtr graphics, uint color, float x, float y, float width, float height) =>
        WithBrush(color, brush => GdipFillRectangle(graphics, brush, x, y, width, height));

    private static void FillDot(IntPtr graphics, uint color, float x, float y) =>
        WithBrush(color, brush => GdipFillEllipse(graphics, brush, x - 1.75f, y - 1.75f, 3.5f, 3.5f));

    [StructLayout(LayoutKind.Sequential)]
    private struct BUTTON_IMAGELIST
    {
        public IntPtr himl;
        public RECT margin;
        public uint uAlign;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, ref BUTTON_IMAGELIST lParam);

    [DllImport("comctl32.dll")]
    private static extern IntPtr ImageList_Create(int width, int height, uint flags, int initial, int grow);

    [DllImport("comctl32.dll")]
    private static extern int ImageList_Add(IntPtr list, IntPtr image, IntPtr mask);

    [DllImport("comctl32.dll")]
    private static extern bool ImageList_Destroy(IntPtr list);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateBitmapFromScan0(int width, int height, int stride, int format, IntPtr scan0, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGetImageGraphicsContext(IntPtr image, out IntPtr graphics);

    [DllImport("gdiplus.dll")]
    private static extern int GdipGraphicsClear(IntPtr graphics, uint color);

    [DllImport("gdiplus.dll")]
    private static extern int GdipScaleWorldTransform(IntPtr graphics, float x, float y, int order);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSetPenLineJoin(IntPtr pen, int join);

    [DllImport("gdiplus.dll")]
    private static extern int GdipFillPolygon(IntPtr graphics, IntPtr brush, GpPointF[] points, int count, int fillMode);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawPolygon(IntPtr graphics, IntPtr pen, GpPointF[] points, int count);

    [DllImport("gdiplus.dll")]
    private static extern int GdipFillRectangle(IntPtr graphics, IntPtr brush, float x, float y, float width, float height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipFillEllipse(IntPtr graphics, IntPtr brush, float x, float y, float width, float height);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawArc(IntPtr graphics, IntPtr pen, float x, float y, float width, float height, float start, float sweep);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDrawLine(IntPtr graphics, IntPtr pen, float x1, float y1, float x2, float y2);
}
