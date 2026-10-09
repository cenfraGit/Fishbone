namespace Fishbone.Tests;

public class FishbonePluginLoaderTests
{
    [Fact]
    public void LoadPlugins_NonExistentDirectory_ReturnsEmptyList()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fishbone-plugin-nonexistent-{Guid.NewGuid():N}");

        var config = new FishboneConfiguration();
        var result = FishbonePluginLoader.LoadPlugins(path, config);

        Assert.Empty(result);
    }

    [Fact]
    public void LoadPlugins_EmptyDirectory_ReturnsEmptyList()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fishbone-plugin-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var config = new FishboneConfiguration();
            var result = FishbonePluginLoader.LoadPlugins(path, config);

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void LoadPlugins_DirectoriesWithNoDlls_ReturnsEmptyList()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fishbone-plugin-nodlls-{Guid.NewGuid():N}");
        string subDir = Path.Combine(path, "MyPlugin");
        Directory.CreateDirectory(subDir);

        try
        {
            var config = new FishboneConfiguration();
            var result = FishbonePluginLoader.LoadPlugins(path, config);

            Assert.Empty(result);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void DefaultPluginsDirectory_FollowsTheVariable()
    {
        // every test assembly sets it, see tests/IsolatedPlugins.cs
        string? isolated = Environment.GetEnvironmentVariable(FishbonePluginLoader.PluginsDirectoryVariable);

        Assert.False(string.IsNullOrEmpty(isolated));
        Assert.Equal(isolated, FishbonePluginLoader.DefaultPluginsDirectory);
    }

    [Fact]
    public void LoadPlugins_RealDlls_RegistersWorkingPlugins_AndSkipsBrokenOnes()
    {
        // the layout the loader expects: one subfolder per plugin. the test plugin's folder also
        // holds a file that isn't an assembly
        string path = Path.Combine(Path.GetTempPath(), $"fishbone-plugin-real-{Guid.NewGuid():N}");
        string test = Directory.CreateDirectory(Path.Combine(path, "test")).FullName;
        string math = Directory.CreateDirectory(Path.Combine(path, "math")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fishbone.TestPlugin.dll"), Path.Combine(test, "Fishbone.TestPlugin.dll"));
        File.WriteAllText(Path.Combine(test, "not-an-assembly.dll"), "garbage");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fishbone.Plugins.Math.dll"), Path.Combine(math, "Fishbone.Plugins.Math.dll"));
        var config = new FishboneConfiguration();

        var loaded = FishbonePluginLoader.LoadPlugins(path, config);

        // the throwing plugin and the one without a parameterless constructor are skipped, and the
        // others still load
        Assert.Equal(["GoodPlugin", "MathPlugin"], loaded.Select(entry => entry.Split(' ')[0]).Order());
        Assert.False(config.BuiltIns.ContainsKey("test_needs_args"));
        var env = FishboneProgram.Run("let good = test_good(); let root = sqrt(16); let pi = PI;", config);
        Assert.Equal(7, env.GetValue("good"));
        Assert.Equal(4.0, env.GetValue("root"));
        Assert.Equal(Math.PI, env.GetValue("pi"));

        // the loaded assemblies stay locked on windows, so the folder is only removed where it can be
        try { Directory.Delete(path, recursive: true); } catch (UnauthorizedAccessException) { } catch (IOException) { }
    }
}