namespace Fishbone.DebugClient;

public sealed record FishboneDapHostCommand(string FileName, IReadOnlyList<string> PrefixArguments);

public interface IFishboneDapHostLocator
{
    FishboneDapHostCommand Locate();
}

public sealed class FishboneDapHostLocator(string? baseDirectory = null) : IFishboneDapHostLocator
{
    private readonly string _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;

    public FishboneDapHostCommand Locate()
    {
        string hostDirectory = Path.Combine(_baseDirectory, "daphost");
        string appHost = Path.Combine(hostDirectory, OperatingSystem.IsWindows() ? "fishbone-dap.exe" : "fishbone-dap");
        if (File.Exists(appHost))
            return new FishboneDapHostCommand(appHost, []);

        string assembly = Path.Combine(hostDirectory, "fishbone-dap.dll");
        if (File.Exists(assembly))
            return new FishboneDapHostCommand("dotnet", [assembly]);

        throw new FileNotFoundException("The bundled fishbone-dap host could not be found.", appHost);
    }
}