// --------------------------------------------------------------------------------
// PluginCommand.cs
//
// "spine plugin ..." , which is how a standalone user gets a plugin.
//
// an app that embeds fishbone references a plugin's nuget package and calls AddPlugin.
// somebody running scripts through the cli has no project to add a reference to, so the
// same package is fetched and unpacked into the plugins folder instead. one channel, two
// ways in, and nothing here is a separate kind of artifact.
// --------------------------------------------------------------------------------

using System.CommandLine;
using Fishbone.Core;
using Fishbone.Hosting;
using Fishbone.Hosting.NuGet;

namespace SpineCLI;

internal static class PluginCommand
{
    public static Command Create()
    {
        var command = new Command("plugin", "Install, list and remove Fishbone plugins.");
        command.Add(CreateInstall());
        command.Add(CreateList());
        command.Add(CreateRemove());
        command.Add(CreateSearch());
        return command;
    }

    private static PluginInstaller Installer() =>
        new(FishbonePluginLoader.DefaultPluginsDirectory);

    // --------------------------------------------------------------------------------
    // install
    // --------------------------------------------------------------------------------

    private static Command CreateInstall()
    {
        var argumentId = new Argument<string>("packageId")
        {
            Description = "The plugin's NuGet package id, for example Fishbone.Plugins.OpenCv.",
        };

        var optionVersion = new Option<string?>("--version")
        {
            Description = "Install this exact version instead of the newest.",
        };

        var optionSource = new Option<string[]>("--source")
        {
            Description = "Feed to install from. Repeat for several. Defaults to the machine's configured sources.",
            AllowMultipleArgumentsPerToken = true,
        };

        var optionPrerelease = new Option<bool>("--prerelease")
        {
            Description = "Allow a prerelease version to be chosen.",
        };

        var command = new Command("install", "Download a plugin and unpack it into the plugins folder.")
        {
            argumentId, optionVersion, optionSource, optionPrerelease,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string packageId = parseResult.GetValue(argumentId)!;
            string[] sources = parseResult.GetValue(optionSource) ?? [];

            PluginInstallResult result = await Installer().InstallAsync(
                packageId,
                parseResult.GetValue(optionVersion),
                sources.Length == 0 ? null : sources,
                parseResult.GetValue(optionPrerelease),
                cancellationToken).ConfigureAwait(false);

            Report(result.Diagnostics);

            if (result.Installed is not { } installed)
                return 1;

            Console.WriteLine($"Installed {installed.PackageId} {installed.Version}");
            Console.WriteLine($"  into {installed.Directory}");
            foreach (string file in installed.Files)
                Console.WriteLine($"  {file}");

            return 0;
        });

        return command;
    }

    // --------------------------------------------------------------------------------
    // list
    // --------------------------------------------------------------------------------

    private static Command CreateList()
    {
        var command = new Command("list", "Show the plugins currently installed.");

        command.SetAction(_ =>
        {
            PluginInstaller installer = Installer();
            IReadOnlyList<InstalledPlugin> installed = installer.List();

            if (installed.Count == 0)
            {
                Console.WriteLine("No plugins installed.");
                Console.WriteLine($"Plugins folder: {FishbonePluginLoader.DefaultPluginsDirectory}");
                return 0;
            }

            foreach (InstalledPlugin plugin in installed)
            {
                // a folder someone copied in by hand has no version marker, and is still a
                // plugin the loader will run, so it is listed rather than hidden
                string version = plugin.Version.Length == 0 ? "(copied in by hand)" : plugin.Version;
                Console.WriteLine($"{plugin.PackageId}  {version}  {plugin.Files.Count} files");
            }

            return 0;
        });

        return command;
    }

    // --------------------------------------------------------------------------------
    // remove
    // --------------------------------------------------------------------------------

    private static Command CreateRemove()
    {
        var argumentId = new Argument<string>("packageId")
        {
            Description = "The plugin to remove, as shown by 'spine plugin list'.",
        };

        var command = new Command("remove", "Delete a plugin's folder.") { argumentId };

        command.SetAction(parseResult =>
        {
            string packageId = parseResult.GetValue(argumentId)!;
            PluginRemoveResult result = Installer().Remove(packageId);

            Report(result.Diagnostics);

            if (!result.Removed)
                return 1;

            Console.WriteLine($"Removed {packageId}");
            return 0;
        });

        return command;
    }

    // --------------------------------------------------------------------------------
    // search
    // --------------------------------------------------------------------------------

    private static Command CreateSearch()
    {
        var argumentTerm = new Argument<string>("term")
        {
            Description = "What to look for.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => string.Empty,
        };

        var optionSource = new Option<string[]>("--source")
        {
            Description = "Feed to search. Repeat for several.",
            AllowMultipleArgumentsPerToken = true,
        };

        var optionPrerelease = new Option<bool>("--prerelease")
        {
            Description = "Include prerelease versions.",
        };

        var command = new Command("search", $"Find packages tagged {PluginInstaller.PluginTag}.")
        {
            argumentTerm, optionSource, optionPrerelease,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string[] sources = parseResult.GetValue(optionSource) ?? [];

            IReadOnlyList<PluginSearchHit> hits = await Installer().SearchAsync(
                parseResult.GetValue(argumentTerm) ?? string.Empty,
                sources.Length == 0 ? null : sources,
                parseResult.GetValue(optionPrerelease),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (hits.Count == 0)
            {
                Console.WriteLine("Nothing found.");
                Console.WriteLine(
                    $"A plugin is findable here once its package carries the {PluginInstaller.PluginTag} tag.");
                return 0;
            }

            foreach (PluginSearchHit hit in hits)
            {
                Console.WriteLine($"{hit.PackageId}  {hit.Version}");
                if (!string.IsNullOrWhiteSpace(hit.Description))
                    Console.WriteLine($"  {Shorten(hit.Description)}");
            }

            return 0;
        });

        return command;
    }

    // --------------------------------------------------------------------------------
    // shared
    // --------------------------------------------------------------------------------

    private static void Report(IReadOnlyList<FishboneDiagnostic> diagnostics)
    {
        foreach (FishboneDiagnostic diagnostic in diagnostics)
        {
            // warnings to stdout and errors to stderr, so piping the output of an install keeps
            // the useful part and a failure still shows up
            TextWriter writer = diagnostic.Severity == DiagnosticSeverity.Error
                ? Console.Error
                : Console.Out;

            writer.WriteLine($"{diagnostic.Severity}: {diagnostic.Message}");
        }
    }

    private static string Shorten(string text)
    {
        string single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 100 ? single : single[..97] + "...";
    }
}
