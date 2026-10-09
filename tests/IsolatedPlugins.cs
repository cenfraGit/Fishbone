using System.Runtime.CompilerServices;

// compiled into every test project by tests/Directory.Build.props. tests, and the fishbone-dap and
// spine processes they start, load plugins from a folder that doesn't exist instead of the
// machine's ~/.fishbone/plugins, so results don't depend on what's installed
internal static class IsolatedPlugins
{
    /// <summary>
    /// The folder plugins came from before the tests changed it, for a manual test that wants
    /// what's installed: the FISHBONE_PLUGINS_DIR that was set, or ~/.fishbone/plugins.
    /// </summary>
    internal static string MachineDirectory { get; private set; } = "";

#pragma warning disable CA2255 // a test assembly is the one place this belongs
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        MachineDirectory = Environment.GetEnvironmentVariable("FISHBONE_PLUGINS_DIR") is { Length: > 0 } set
            ? set
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fishbone", "plugins");
        Environment.SetEnvironmentVariable("FISHBONE_PLUGINS_DIR", Path.Combine(Path.GetTempPath(), "fishbone-tests-no-plugins"));
    }
}
