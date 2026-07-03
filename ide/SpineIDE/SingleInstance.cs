// --------------------------------------------------------------------------------
// SingleInstance.cs
//
// Makes SpineIDE a single-instance application. The first process to start owns a
// named mutex and listens on a named pipe; any later launch forwards its startup
// options (attach port / file path) over the pipe and exits, so the running window
// is reused instead of opening a new one. This is what lets FlexInspect's debug
// button re-trigger a debug session in the already-open IDE: each press launches
// SpineIDE.exe --attach <port>, and the secondary process hands the port to the
// primary, whose window re-attaches in place.
// --------------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpineIDE;

internal static class SingleInstance
{
    // Local\ scope: one primary per interactive session; username keeps parallel
    // sessions of different users on the same machine from colliding.
    private static readonly string _name = $"SpineIDE.SingleInstance.{Environment.UserName}";
    private static string MutexName => @"Local\" + _name;
    private static string PipeName => _name;

    private static Mutex? _mutex; // held for the primary's lifetime, released by process exit

    /// <summary>True when this process won the mutex and owns the pipe server.</summary>
    public static bool IsPrimary { get; private set; }

    /// <summary>
    /// Attempts to claim the single-instance mutex. The first caller per session wins
    /// and should start the pipe server; later processes should forward and exit.
    /// </summary>
    public static bool TryBecomePrimary()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            _mutex = mutex;
            IsPrimary = true;
            return true;
        }

        mutex.Dispose();
        return false;
    }

    /// <summary>
    /// Forwards <paramref name="options"/> to the running primary instance.
    /// Returns false if the primary could not be reached (e.g. it is shutting down),
    /// in which case the caller should proceed with a normal startup.
    /// </summary>
    public static bool TrySignalPrimary(SpineIdeStartupOptions options)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            // generous enough for a primary that is still booting its server loop
            client.Connect(TimeSpan.FromSeconds(3));
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(options));
            client.WaitForPipeDrain();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the primary's pipe server. Each forwarded startup is delivered to
    /// <paramref name="onReceived"/> (from a background thread — marshal to the UI
    /// thread in the callback). Dispose the returned handle to stop the server.
    /// </summary>
    public static IDisposable StartServer(Action<SpineIdeStartupOptions> onReceived)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(() => ServeAsync(onReceived, cts.Token), CancellationToken.None);
        return new ServerHandle(cts);
    }

    private static async Task ServeAsync(Action<SpineIdeStartupOptions> onReceived, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                var options = JsonSerializer.Deserialize<SpineIdeStartupOptions>(line);
                if (options is not null)
                    onReceived(options);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // a malformed message or broken pipe must not kill the server loop
            }
        }
    }

    private sealed class ServerHandle(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            cts.Dispose();
            // the mutex is intentionally NOT released here: it belongs to the process
            // lifetime (released by the OS on exit), and ReleaseMutex would throw if
            // called from a thread other than the one that acquired it.
        }
    }
}
