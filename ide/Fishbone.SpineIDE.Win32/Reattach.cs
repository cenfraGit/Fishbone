using Fishbone.Debugging;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace SpineIDE.Win32;

internal static partial class Program
{
    // a SpineIDE a host opened stays open after the run, and attaches to the host's next run
    // when it's asked on the pipe. while it's debugging, or holds unsaved changes, it says it's
    // busy, and the host opens another
    private static void ListenForReattach() => _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(SpineIdeAttachPipe.Name, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                bool attached = false;
                if ((await reader.ReadLineAsync())?.Split(' ') is ["attach", var number] && int.TryParse(number, out int port))
                {
                    var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Post(() =>
                    {
                        bool idle = !_running && !IsModified;
                        if (idle)
                        {
                            BringToFront();
                            Attach(port);
                        }
                        answer.SetResult(idle);
                    });
                    attached = await answer.Task;
                }
                await writer.WriteLineAsync(attached ? "ok" : "busy");
            }
            catch (Exception)
            {
                // a host that went away mid request. wait a moment, so a pipe that can't be made doesn't spin
                await Task.Delay(500);
            }
        }
    });

    private static void BringToFront()
    {
        if (IsIconic(_window))
            ShowWindow(_window, 9); // SW_RESTORE
        SetForegroundWindow(_window);
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
