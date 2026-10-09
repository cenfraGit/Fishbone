using SpineIDE.Services;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private const uint MF_STRING = 0x0, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;

    // menu command ids. the run and debug buttons share theirs
    private const int CommandNew = 100, CommandOpen = 101, CommandSave = 102, CommandSaveAs = 103, CommandExit = 104,
        CommandContinue = 110, CommandPause = 111, CommandStop = 112, CommandStepOver = 113, CommandStepInto = 114,
        CommandStepOut = 115, CommandBreakpoint = 116, CommandFullScreen = 120, CommandUncheckAll = 121, CommandSampleFirst = 1000;

    private static IntPtr CreateMainMenu()
    {
        IntPtr file = CreatePopupMenu();
        AppendMenuW(file, MF_STRING, CommandNew, "&New\tCtrl+N");
        AppendMenuW(file, MF_STRING, CommandOpen, "&Open...\tCtrl+O");
        AppendMenuW(file, MF_STRING, CommandSave, "&Save\tCtrl+S");
        AppendMenuW(file, MF_STRING, CommandSaveAs, "Save &As...\tCtrl+Shift+S");
        AppendMenuW(file, MF_SEPARATOR, 0, null);
        AppendMenuW(file, MF_STRING, CommandExit, "E&xit");

        IntPtr debug = CreatePopupMenu();
        AppendMenuW(debug, MF_STRING, RunButtonId, "&Run\tCtrl+F5");
        AppendMenuW(debug, MF_STRING, DebugButtonId, "&Debug\tF5");
        AppendMenuW(debug, MF_STRING, CommandContinue, "&Continue\tF5");
        AppendMenuW(debug, MF_STRING, CommandPause, "&Pause");
        AppendMenuW(debug, MF_STRING, CommandStop, "S&top\tShift+F5");
        AppendMenuW(debug, MF_SEPARATOR, 0, null);
        AppendMenuW(debug, MF_STRING, CommandStepOver, "Step &Over\tF10");
        AppendMenuW(debug, MF_STRING, CommandStepInto, "Step &Into\tF11");
        AppendMenuW(debug, MF_STRING, CommandStepOut, "Step O&ut\tShift+F11");
        AppendMenuW(debug, MF_SEPARATOR, 0, null);
        AppendMenuW(debug, MF_STRING, CommandBreakpoint, "Toggle &Breakpoint\tF9");

        IntPtr samples = CreatePopupMenu();
        for (int i = 0; i < SampleCatalog.Samples.Count; i++)
            AppendMenuW(samples, MF_STRING, CommandSampleFirst + i, SampleCatalog.Samples[i].DisplayName);
        IntPtr help = CreatePopupMenu();
        AppendMenuW(help, MF_POPUP, samples, "&Samples");

        IntPtr menu = CreateMenu();
        AppendMenuW(menu, MF_POPUP, file, "&File");
        AppendMenuW(menu, MF_POPUP, debug, "&Debug");
        AppendMenuW(menu, MF_POPUP, help, "&Help");
        return menu;
    }

    // menu clicks, button clicks and shortcuts all end up here
    private static void RunCommand(int command)
    {
        switch (command)
        {
            case CommandNew: NewFile(); break;
            case CommandOpen: OpenFile(); break;
            case CommandSave: Save(); break;
            case CommandSaveAs: SaveAs(); break;
            case CommandExit: SendMessageW(_window, WM_CLOSE, 0, 0); break;
            case RunButtonId: Run(); break;
            case DebugButtonId: DebugOrContinue(); break;
            case CommandContinue: if (_paused) _ = _session.ContinueAsync(); break;
            case CommandPause: if (_running && !_paused) _ = _session.PauseAsync(); break;
            case CommandStop: _ = _session.StopAsync(); break;
            case CommandStepOver: if (_paused) _ = _session.StepOverAsync(); break;
            case CommandStepInto: if (_paused) _ = _session.StepIntoAsync(); break;
            case CommandStepOut: if (_paused) _ = _session.StepOutAsync(); break;
            case CommandBreakpoint: ToggleBreakpoint(CaretLine()); break;
            case CommandFullScreen: ShowFullScreen(); break;
            case CommandUncheckAll: UncheckAll(); break;
            case >= CommandSampleFirst when command - CommandSampleFirst < SampleCatalog.Samples.Count:
                OpenSample(command - CommandSampleFirst);
                break;
        }
    }
}
