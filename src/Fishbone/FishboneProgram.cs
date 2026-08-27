// --------------------------------------------------------------------------------
// FishboneProgram.cs
//
// the entry point for fishbone code execution.
// --------------------------------------------------------------------------------

using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Interpreter;
using Fishbone.Parser;
using System.Security.Cryptography;
using System.Text;

namespace Fishbone;

/// <summary>
/// A Fishbone program that can be executed one or multiple times.
/// </summary>
public sealed class FishboneProgram
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private readonly AstNode? _ast;

    public string SourceCode { get; } = string.Empty;
    public string SourceName { private set; get; } = "New";  // display name for source
    public string SourceIdentity { get; } = string.Empty;    // SHA256 for program

    // --------------------------------------------------------------------------------
    // constructors
    //
    // note: there's commonly only two ways to initialize a program:
    //
    // - by passing source code
    // - by specifying a file to read code from
    //
    // therefore, we'd like two constructors that can achieve
    // this. but since both the sourceCode and filePath are of type
    // "string", we can't achieve this cleanly with constructors
    //
    // so we'll have a single private constructor, and use static
    // factory methods (explicitly named) to instantiate a program via
    // either source code or a file path.
    // --------------------------------------------------------------------------------

    private FishboneProgram(string sourceCode)
    {
        _ast = FromSourceCodeCode(sourceCode);
        SourceCode = sourceCode;
        SourceIdentity = ComputeIdentity(sourceCode);
    }

    public static FishboneProgram FromSourceCode(string sourceCode)
    {
        return new FishboneProgram(sourceCode);
    }

    public static FishboneProgram FromFile(string filePath)
    {
        var sourceCode = ReadFile(filePath);
        var program = new FishboneProgram(sourceCode);
        program.SourceName = Path.GetFileName(filePath);
        return program;
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    /// <summary>
    /// Executes the program and returns the FishboneEnvironment after evaluation.
    /// </summary>
    public FishboneEnvironment Run(FishboneConfiguration? configuration = null,
                                   IFishboneDebugger? debugger = null,
                                   CancellationToken cancellationToken = default)
    {
        // an empty config is a valid run: the script just gets no built-ins or values
        configuration ??= new FishboneConfiguration();

        // create new environment used for this run
        var env = new FishboneEnvironment();

        if (_ast is null)
            return env;

        // seed the environment from the config
        foreach (var builtIn in configuration.BuiltIns)
            env.AddBuiltIn(builtIn.Key, builtIn.Value);

        foreach (var val in configuration.Values)
            env.Declare(val.Key, val.Value);

        // determine debugger
        var activeDebugger = debugger ?? NullFishboneDebugger.Instance;

        // initialize interpreter
        var interpreter = new FishboneInterpreter(cancellationToken,
                                                  activeDebugger,
                                                  configuration.TypeConverters,
                                                  configuration.EnableMemberAccess);

        // start execution
        activeDebugger.OnExecutionStarted(_ast, env);

        try
        {
            interpreter.Evaluate(env, _ast);
        }
        finally
        {
            activeDebugger.OnExecutionCompleted(env);
        }

        return env;
    }

    /// <summary>
    /// Directly executes the source code in the string, returning 
    /// a FishboneEnvironment.
    /// </summary>
    public static FishboneEnvironment Run(string sourceCode,
                                          FishboneConfiguration? configuration = null,
                                          IFishboneDebugger? debugger = null,
                                          CancellationToken cancellationToken = default)
    {
        var program = FishboneProgram.FromSourceCode(sourceCode);
        return program.Run(configuration, debugger, cancellationToken);
    }

    // --------------------------------------------------------------------------------
    // helper methods
    // --------------------------------------------------------------------------------

    internal static AstNode FromSourceCodeCode(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        var ast = ASTParser.Parse(sourceCode);
        return ast;
    }

    internal static string ReadFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return File.ReadAllText(filePath, Encoding.UTF8);
    }

    // 64-character hex string used for cache invalidation. different
    // source texts produce different identities (even if same AST)
    private static string ComputeIdentity(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
}