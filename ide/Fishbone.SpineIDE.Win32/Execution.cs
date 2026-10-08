using Fishbone.DebugClient;
using SpineIDE.Services;
using System.Diagnostics;
using System.Text;

namespace SpineIDE.Win32;

internal static partial class Program
{
    private static ScriptSession _session = null!;
    private static readonly Stopwatch _runClock = new();

    // only touched on the ui thread
    private static bool _running;
    private static bool _paused;

    // the session raises its events on any thread, so each one is posted to the ui thread
    private static void SetupSession()
    {
        _session = new ScriptSession(new FishboneDebugClientSessionFactory(new FishboneDapHostLocator()));
        _session.Started += () => Post(() =>
        {
            SetWindowTextW(_output, "");
            SetWindowTextW(_variables, "");
        });
        _session.Output += text => Post(() => AppendOutput(text));
        _session.Paused += (snapshot, _, isProgramExit) => Post(() => OnPaused(snapshot, isProgramExit));
        _session.Continued += () => Post(() =>
        {
            _paused = false;
            ShowCurrentLine(-1);
            SetWindowTextW(_status, "running...");
        });
    }

    private static async void Run()
    {
        if (_running)
            return;
        BeginExecution("running...");

        // input() comes in a later step
        ScriptRunOutcome? outcome = await _session.RunAsync(Encoding.UTF8.GetString(GetEditorBytes()), null, _ =>
            Task.FromException<string>(new NotSupportedException("input() isn't supported in SpineIDE for Windows yet.")));
        Post(() => EndExecution(outcome));
    }

    private static async void DebugOrContinue()
    {
        if (_paused)
        {
            _ = _session.ContinueAsync();
            return;
        }
        if (_running)
            return;
        BeginExecution("starting the debugger...");

        // the debug host runs a script from a file, so it gets a scratch copy of the editor text
        string path = Path.Combine(Path.GetTempPath(), "spineide", "script.fb");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GetEditorBytes());

        ScriptRunOutcome? outcome = await _session.DebugAsync(path, BreakpointLines(), _ => { });
        Post(() =>
        {
            _paused = false;
            ShowCurrentLine(-1);
            EndExecution(outcome);
        });
    }

    private static void BeginExecution(string status)
    {
        _running = true;
        _runClock.Restart();
        SetWindowTextW(_status, status);
    }

    private static void EndExecution(ScriptRunOutcome? outcome)
    {
        _running = false;
        // null means a newer run replaced this one, and that one reports itself
        if (outcome is null)
            return;

        foreach (ScriptExecutionError error in outcome.Errors)
            AppendOutput(error.HasLocation
                ? $"error at {error.LocationDisplay.ToLowerInvariant()}: {error.ExMessage}{Environment.NewLine}"
                : $"error: {error.ExMessage}{Environment.NewLine}");

        string result = outcome.Errors.Count > 0 ? "finished with errors" : "finished";
        SetWindowTextW(_status, $"{result} in {_runClock.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static void OnPaused(FishbonePauseSnapshot snapshot, bool isProgramExit)
    {
        FishboneDebugFrame? frame = snapshot.Frames.FirstOrDefault();
        ShowVariables(frame);
        _paused = true;

        // the pause after the last statement only shows the final values. it stays until the
        // user continues, so they can still be looked at
        if (isProgramExit)
        {
            ShowCurrentLine(-1);
            SetWindowTextW(_status, "the script finished. F5 ends the session");
            return;
        }

        if (frame is not null)
            ShowCurrentLine(frame.Line - 1);
        SetWindowTextW(_status, $"paused ({snapshot.Reason}) at line {frame?.Line}");
    }

    private static void ShowVariables(FishboneDebugFrame? frame)
    {
        if (frame is null)
            return;

        var text = new StringBuilder();
        foreach (FishboneDebugScope scope in frame.Scopes)
        {
            text.AppendLine(scope.Name);
            foreach (FishboneDebugVariable variable in scope.Variables)
                text.AppendLine($"    {variable.Name} = {variable.Value}");
        }
        SetWindowTextW(_variables, text.ToString());
    }

    private static void AppendOutput(string text)
    {
        int end = GetWindowTextLengthW(_output);
        SendMessageW(_output, EM_SETSEL, end, end);
        SendMessageW(_output, EM_REPLACESEL, 0, text.ReplaceLineEndings("\r\n"));
    }
}
