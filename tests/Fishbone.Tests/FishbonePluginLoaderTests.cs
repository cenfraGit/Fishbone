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
}