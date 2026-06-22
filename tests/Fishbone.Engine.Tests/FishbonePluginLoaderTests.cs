namespace Fishbone.Engine.Tests;

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
    public void DefaultPluginsDirectory_UsesUserProfile()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".fishbone", "plugins");

        Assert.Equal(expected, FishbonePluginLoader.DefaultPluginsDirectory);
    }
}