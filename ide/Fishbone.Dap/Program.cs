using System.CommandLine;

namespace Fishbone.Dap;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var scriptArgument = new Argument<FileInfo>("scriptPath")
        {
            Description = "The Fishbone script to debug.",
            Arity = ArgumentArity.ExactlyOne
        };
        scriptArgument.AcceptExistingOnly();
        var portOption = new Option<int>("--port")
        {
            Description = "The loopback TCP port. Use 0 to select an available port.",
            DefaultValueFactory = _ => 4711
        };
        var root = new RootCommand("Standalone Fishbone Debug Adapter Protocol server.");
        root.Add(scriptArgument);
        root.Add(portOption);
        root.SetAction(async (parseResult, cancellationToken) =>
        {
            var script = parseResult.GetValue(scriptArgument)!;
            int port = parseResult.GetValue(portOption);
            if (port is < 0 or > 65535)
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 0 and 65535.");
            return await new FishboneDapHost().RunAsync(script.FullName, port, Console.Out, cancellationToken);
        });
        return await root.Parse(args).InvokeAsync();
    }
}