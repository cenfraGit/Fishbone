using Fishbone.Engine;
using System.CommandLine;

namespace Fishbone.CLI;

internal class Program
{
    private static int Main(string[] args)
    {
        // ------------------------------------------------------------ arguments

        var argumentScript = new Argument<FileInfo>(name: "scriptPath")
        {
            Description = "The path to the fishbone script to execute.",
            Arity = ArgumentArity.ExactlyOne,
        };
        argumentScript.AcceptExistingOnly(); // file must exist

        // ------------------------------------------------------------ options

        var optionShowTable = new Option<bool>(name: "--show-table")
        {
            Description = "Display the fishbone environment's variables after execution."
        };

        var rootCommand = new RootCommand("Fishbone script runner.");
        rootCommand.Add(argumentScript);
        rootCommand.Add(optionShowTable);

        // ------------------------------------------------------------ actions

        rootCommand.SetAction(parseResult =>
        {
            FileInfo scriptFile = parseResult.GetValue(argumentScript)!;
            bool showTable = parseResult.GetValue(optionShowTable);

            ExecuteScript(scriptFile, showTable);
        });

        ParseResult parseResult = rootCommand.Parse(args);

        return parseResult.Invoke();
    }

    static void ExecuteScript(FileInfo script, bool showTable)
    {
        if (!File.Exists(script.FullName))
            throw new FileNotFoundException(script.FullName);

        var contents = File.ReadAllText(script.FullName);
        var config = new FishboneConfiguration();
        var env = FishboneEngine.Run(contents, config);

        if (showTable)
        {
            foreach (var variable in env.Values)
            {
                Console.WriteLine($"{variable.Key} : {variable.Value}");
            }
        }
    }
}