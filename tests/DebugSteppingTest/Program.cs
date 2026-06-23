using Fishbone.DebugAdapter;
using Fishbone.Engine;
using System.Net;

var internalDict = new Dictionary<string, object>
{
    { "InternalValue", 0 }
};

var config = new FishboneConfiguration(injectDefaults: true);

config.RegisterBuiltIn("InternalDict", internalDict);

config.RegisterBuiltIn("SetInternalValue", new Action<object>(value =>
    internalDict["InternalValue"] = value));

int DoSomething()
{
    return 10;
}

config.RegisterBuiltIn("DoSomething", DoSomething);

void TrySomething(out int a)
{
    a = 10;
}

config.RegisterBuiltIn("TrySomething", TrySomething);

string scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "test.fb");
if (!File.Exists(scriptPath))
    scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "test.fb");

string source = await File.ReadAllTextAsync(scriptPath);
Console.Error.WriteLine($"Script: {scriptPath}");

await using var server = await FishboneDebugServer.StartAsync(
    new FishboneDebugServerOptions
    {
        SourceCode = source,
        SourceName = "test.fb",
        SourceIdentity = scriptPath,
        Configuration = config,
        ListenEndpoint = new IPEndPoint(IPAddress.Loopback, 0),
        RedirectOutput = true,
    });

Console.WriteLine($"DEBUG PORT: {server.Endpoint.Port}");

var result = await server.Completion;

Console.WriteLine($"Exit code: {result.ExitCode}");
if (result.Error is not null)
    Console.Error.WriteLine($"Error: {result.Error.Message}");