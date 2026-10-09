using Fishbone.Debugging;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;

namespace Fishbone.DebugAdapter;

/// <summary>
/// Opens SpineIDE on a debug server. A SpineIDE an earlier run opened, that's idle, attaches
/// again, so a host that debugs several runs keeps one window. Otherwise a new one starts.
/// </summary>
public static class SpineIdeLauncher
{
    /// <param name="executable">The SpineIDE to start when none can be reused. By default it's
    /// found the way <see cref="FishboneDebugOptions.IdeLauncher"/> describes.</param>
    /// <returns>The started process, or null when an open SpineIDE attached.</returns>
    public static Process? Launch(IPEndPoint endpoint, string? executable = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (TryReuse(endpoint.Port))
            return null;
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable ?? FishboneProgramDebugExtensions.ResolveSpineIde(),
            Arguments = $"--attach {endpoint.Port}",
            UseShellExecute = false,
        });
    }

    private static bool TryReuse(int port)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", SpineIdeAttachPipe.Name, PipeDirection.InOut);
            pipe.Connect(200);
            // so the window it brings back can come to the front
            if (OperatingSystem.IsWindows())
                AllowSetForegroundWindow(-1); // ASFW_ANY
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer.WriteLine($"attach {port}");
            Task<string?> answer = reader.ReadLineAsync();
            return answer.Wait(TimeSpan.FromSeconds(2)) && answer.Result == "ok";
        }
        catch (Exception)
        {
            // no SpineIDE is waiting, or it went away
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
