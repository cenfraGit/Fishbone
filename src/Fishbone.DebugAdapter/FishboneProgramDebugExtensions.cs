using System.Diagnostics;
using System.Net;
using Fishbone.Engine;

namespace Fishbone.DebugAdapter;

/// <summary>
/// Adds debuggable execution to <see cref="FishboneProgram"/>. This lives in the debug-adapter
/// package so the core engine never depends on the Debug Adapter Protocol server or networking;
/// reference this package and add <c>using Fishbone.DebugAdapter;</c> to opt into the capability.
/// </summary>
public static class FishboneProgramDebugExtensions
{
    /// <summary>
    /// Runs the program with an optional attached debugger/IDE. A debug server is opened on an
    /// ephemeral loopback port; if a client attaches within <see cref="FishboneDebugOptions.AttachTimeout"/>
    /// the program runs under the debugger, otherwise it falls back to a normal headless run.
    /// This call never hangs waiting for a debugger.
    /// </summary>
    public static async Task<FishboneRunResult> RunDebuggableAsync(
        this FishboneProgram program,
        FishboneConfiguration configuration,
        FishboneDebugOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(configuration);
        options ??= new FishboneDebugOptions();

        var serverOptions = new FishboneDebugServerOptions
        {
            SourceCode = program.Source,
            SourceName = options.SourceName ?? program.SourceName,
            SourceIdentity = program.SourceIdentity,
            Configuration = configuration,
            RedirectOutput = options.RedirectOutput,
            ListenEndpoint = options.ListenEndpoint ?? new IPEndPoint(IPAddress.Loopback, 0),
        };

        await using var session = await FishboneDebugServer.StartAsync(serverOptions, cancellationToken).ConfigureAwait(false);

        if (options.OpenIde)
        {
            var launcher = options.IdeLauncher ?? DefaultIdeLauncher;
            // a missing or failed IDE is not fatal: we still wait for the attach timeout (an external
            // client may attach) and otherwise fall back to a headless run.
            try { launcher(session.Endpoint); }
            catch { }
        }

        await Task.WhenAny(session.ClientConnected, Task.Delay(options.AttachTimeout)).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            await session.StopAsync().ConfigureAwait(false);
            return new FishboneRunResult(environment: null, error: null, debuggerAttached: false, wasCancelled: true);
        }

        if (!session.ClientConnected.IsCompletedSuccessfully)
        {
            // nobody attached in time, tear the server down and run headless
            await session.StopAsync().ConfigureAwait(false);
            var headlessEnvironment = program.Run(configuration, cancellationToken);
            return new FishboneRunResult(headlessEnvironment, error: null, debuggerAttached: false, wasCancelled: false);
        }

        var result = await session.Completion.ConfigureAwait(false);
        return new FishboneRunResult(result.Environment, result.Error, debuggerAttached: true, result.WasCancelled);
    }

    private static Process? DefaultIdeLauncher(IPEndPoint endpoint)
    {
        var executable = ResolveSpineIde();
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = $"--attach {endpoint.Port}",
            UseShellExecute = false,
        });
    }

    private static string ResolveSpineIde()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("SPINEIDE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
            return fromEnvironment;

        var executableName = OperatingSystem.IsWindows() ? "SpineIDE.exe" : "SpineIDE";
        var besideHost = Path.Combine(AppContext.BaseDirectory, executableName);
        if (File.Exists(besideHost))
            return besideHost;

        // last resort is to let the OS resolve it on PATH
        return executableName;
    }
}