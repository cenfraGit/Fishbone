using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;

namespace SpineIDE;

public sealed record SpineIdeStartupOptions(int? AttachPort = null, string? FilePath = null)
{
    public static bool TryParse(string[] args, TextWriter errorWriter, out SpineIdeStartupOptions options)
    {
        var attachOption = new Option<int?>("--attach")
        {
            Description = "Attach to a Fishbone DAP server on localhost.",
            Arity = ArgumentArity.ExactlyOne
        };
        var fileArgument = new Argument<string?>("file")
        {
            Description = "Path to a Fishbone script (.fb) file to open on startup.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var root = new RootCommand("SpineIDE Fishbone development environment.");
        root.Add(attachOption);
        root.Add(fileArgument);
        ParseResult result = root.Parse(args);
        if (result.Errors.Count > 0)
        {
            foreach (ParseError error in result.Errors)
                errorWriter.WriteLine(error.Message);
            options = new SpineIdeStartupOptions();
            return false;
        }

        int? port = result.GetValue(attachOption);
        if (port is not null && port is < 1 or > 65535)
        {
            errorWriter.WriteLine("Option '--attach': Port must be between 1 and 65535.");
            options = new SpineIdeStartupOptions();
            return false;
        }

        string? filePath = result.GetValue(fileArgument);
        if (filePath is not null && !File.Exists(filePath))
        {
            errorWriter.WriteLine($"File not found: {filePath}");
            options = new SpineIdeStartupOptions();
            return false;
        }

        options = new SpineIdeStartupOptions(port, filePath);
        return true;
    }
}