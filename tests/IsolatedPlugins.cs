using System.Runtime.CompilerServices;

// compiled into every test project by tests/Directory.Build.props. tests, and the fishbone-dap and
// spine processes they start, load plugins from a folder that doesn't exist instead of the
// machine's ~/.fishbone/plugins, so results don't depend on what's installed
internal static class IsolatedPlugins
{
#pragma warning disable CA2255 // a test assembly is the one place this belongs
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize() =>
        Environment.SetEnvironmentVariable("FISHBONE_PLUGINS_DIR", Path.Combine(Path.GetTempPath(), "fishbone-tests-no-plugins"));
}
