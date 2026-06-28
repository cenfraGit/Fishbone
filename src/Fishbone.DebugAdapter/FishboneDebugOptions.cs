using System.Diagnostics;
using System.Net;

namespace Fishbone.DebugAdapter;

/// <summary>
/// Options for a debuggable run via <c>FishboneProgram.RunDebuggableAsync</c>. Holds only the
/// debug/host concerns; what a script can see comes from the <c>FishboneConfiguration</c> argument.
/// </summary>
public sealed class FishboneDebugOptions
{
    /// <summary>
    /// When true, the default (or supplied) <see cref="IdeLauncher"/> is invoked to launch an IDE
    /// pointed at the debug server. When false, the server still opens so an external client may
    /// attach within <see cref="AttachTimeout"/>.
    /// </summary>
    public bool OpenIde { get; init; }

    /// <summary>
    /// How long to wait for a debug client to attach before falling back to a normal headless run.
    /// </summary>
    public TimeSpan AttachTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Overrides the program's source name shown in the debugger tab.</summary>
    public string? SourceName { get; init; }

    /// <summary>The endpoint the debug server listens on. Defaults to an ephemeral loopback port.</summary>
    public IPEndPoint? ListenEndpoint { get; init; }

    /// <summary>When true, script output is redirected to the debug client.</summary>
    public bool RedirectOutput { get; init; } = true;

    /// <summary>
    /// Launches an IDE/debug client pointed at the given endpoint, returning the launched process
    /// (or null if it could not be launched, in which case the run falls back to headless). When
    /// null, a default launcher locates SpineIDE via the <c>SPINEIDE_PATH</c> environment variable,
    /// a path next to the host application, or <c>PATH</c>.
    /// </summary>
    public Func<IPEndPoint, Process?>? IdeLauncher { get; init; }
}