using Fishbone.DebugClient;

namespace Fishbone.DebugClient.Tests;

public class FishboneDapHostLocatorTests
{
    [Fact]
    public void UsesBundledAppHostWhenPresent()
    {
        string directory = CreateHostDirectory();
        string appHost = Path.Combine(directory, "daphost", OperatingSystem.IsWindows() ? "fishbone-dap.exe" : "fishbone-dap");
        File.WriteAllText(appHost, string.Empty);

        FishboneDapHostCommand command = new FishboneDapHostLocator(directory).Locate();

        Assert.Equal(appHost, command.FileName);
        Assert.Empty(command.PrefixArguments);
    }

    [Fact]
    public void FallsBackToDotnetAndBundledAssembly()
    {
        string directory = CreateHostDirectory();
        string assembly = Path.Combine(directory, "daphost", "fishbone-dap.dll");
        File.WriteAllText(assembly, string.Empty);

        FishboneDapHostCommand command = new FishboneDapHostLocator(directory).Locate();

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal([assembly], command.PrefixArguments);
    }

    private static string CreateHostDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"fishbone-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "daphost"));
        return directory;
    }
}