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
            ClearVariables();
        });
        _session.Output += text => Post(() => AppendOutput(text));
        _session.Paused += (snapshot, session, isProgramExit) => Post(() => OnPaused(snapshot, session, isProgramExit));
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

        string? directory = _filePath is null ? null : Path.GetDirectoryName(_filePath);
        ScriptRunOutcome? outcome = await _session.RunAsync(Encoding.UTF8.GetString(GetEditorBytes()), directory, ReadInput);
        Post(() => EndExecution(outcome));
    }

    private static async void DebugOrContinue()
    {
        if (_paused)
        {
            _ = _session.ContinueAsync();
            return;
        }
        // the debug host runs the script from its file, so it's saved first
        if (_running || !Save())
            return;
        BeginExecution("starting the debugger...");

        ScriptRunOutcome? outcome = await _session.DebugAsync(_filePath!, BreakpointLines(), _ => { });
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

        if (outcome.Environment is not null)
            ShowFinalVariables(outcome.Environment);

        string result = outcome.Errors.Count > 0 ? "finished with errors" : "finished";
        SetWindowTextW(_status, $"{result} in {_runClock.Elapsed.TotalMilliseconds:F0} ms");
    }

    private static void OnPaused(FishbonePauseSnapshot snapshot, IFishboneDebugClientSession session, bool isProgramExit)
    {
        FishboneDebugFrame? frame = snapshot.Frames.FirstOrDefault();
        ShowDebugVariables(frame, session);
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

    // --------------------------------------------------------------------------------
    // input()
    // --------------------------------------------------------------------------------

    private static TaskCompletionSource<string>? _inputAnswer;

    // the session calls this on the script's thread, and waits on the answer
    private static Task<string> ReadInput(CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            _inputAnswer = answer;
            EnableWindow(_input, true);
            SetFocus(_input);
            SetWindowTextW(_status, "waiting for input...");
        });
        cancellationToken.Register(() => Post(() => EndInput(null)));
        return answer.Task;
    }

    private static void SubmitInput()
    {
        var text = new StringBuilder(GetWindowTextLengthW(_input) + 1);
        GetWindowTextW(_input, text, text.Capacity);
        EndInput(text.ToString());
    }

    // null means the script stopped waiting
    private static void EndInput(string? line)
    {
        if (_inputAnswer is null)
            return;
        if (line is null)
            _inputAnswer.TrySetCanceled();
        else
            _inputAnswer.TrySetResult(line);
        _inputAnswer = null;
        SetWindowTextW(_input, "");
        EnableWindow(_input, false);
        SetFocus(_editor);
        SetWindowTextW(_status, "running...");
    }

    private static void AppendOutput(string text)
    {
        int end = GetWindowTextLengthW(_output);
        SendMessageW(_output, EM_SETSEL, end, end);
        SendMessageW(_output, EM_REPLACESEL, 0, text.ReplaceLineEndings("\r\n"));
    }
}
