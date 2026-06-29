using System.Security.Cryptography;
using System.Text;
using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Interpreter;
using Fishbone.Parser;

namespace Fishbone.Engine;

/// <summary>
/// A parsed, immutable Fishbone program. Parse once, execute many: each <see cref="Run"/> builds a
/// fresh environment, so the same program can be reused across runs (and threads) while the data
/// around it changes.
/// </summary>
public sealed class FishboneProgram
{
    private readonly AstNode _ast;

    private FishboneProgram(AstNode ast, string source, string sourceName)
    {
        _ast = ast;
        Source = source;
        SourceName = sourceName;
        SourceIdentity = ComputeIdentity(source);
    }

    // the original source text
    public string Source { get; }

    // display name for source
    public string SourceName { get; }

    public string SourceIdentity { get; }

    /// <summary>Parses source text into a reusable program.</summary>
    public static FishboneProgram ParseSource(string source, string sourceName = "script")
    {
        ArgumentNullException.ThrowIfNull(source);
        var ast = ASTParser.Parse(source);
        return new FishboneProgram(ast, source, sourceName);
    }

    /// <summary>
    /// Reads a script file as UTF-8 and parses it. The file name becomes
    /// the program's <see cref="SourceName"/>.
    /// </summary>
    public static FishboneProgram ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = File.ReadAllText(path, Encoding.UTF8);
        return ParseSource(source, Path.GetFileName(path));
    }

    /// <summary>
    /// Executes the program against a fresh environment seeded from <paramref name="configuration"/>.
    /// Built-ins are added as ambient names; values are declared as script variables.
    /// </summary>
    public FishboneEnvironment Run(
        FishboneConfiguration configuration,
        CancellationToken cancellationToken = default,
        IFishboneDebugger? debugger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var envRoot = new FishboneEnvironment();
        foreach (var builtIn in configuration.BuiltIns)
            envRoot.AddBuiltIn(builtIn.Key, builtIn.Value);
        foreach (var value in configuration.Values)
            envRoot.Declare(value.Key, value.Value);

        var activeDebugger = debugger ?? NullFishboneDebugger.Instance;
        var interpreter = new FishboneInterpreter(cancellationToken, activeDebugger, configuration.TypeConverters);
        activeDebugger.OnExecutionStarted(_ast, envRoot);
        try
        {
            interpreter.Evaluate(envRoot, _ast);
        }
        finally
        {
            activeDebugger.OnExecutionCompleted(envRoot);
        }

        return envRoot;
    }

    private static string ComputeIdentity(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}