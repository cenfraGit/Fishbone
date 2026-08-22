// --------------------------------------------------------------------------------
// FishboneEngine.cs
//
// static class with Run method to initialize the execution of a script. takes
// in the source code and FishboneConfiguration to execute.
//
// note: mainly for one-time execution.
// --------------------------------------------------------------------------------

using Fishbone.Core;
using Fishbone.Debugging;
using Fishbone.Parser;

namespace Fishbone.Engine;

public static class FishboneEngine
{
    /// <summary>
    /// Parses and runs a script in one call. Convenience for run-once use; for repeated execution
    /// of the same script, parse with <see cref="FishboneProgram.ParseSource"/> once and reuse it.
    /// </summary>
    public static FishboneEnvironment Run(string sourceCode,
                                          FishboneConfiguration configuration,
                                          CancellationToken cancellationToken = default,
                                          IFishboneDebugger? debugger = null) =>
        FishboneProgram.ParseSource(sourceCode).Run(configuration, cancellationToken, debugger);

    /// <summary>
    /// Checks a script for syntax errors without running it, returning what is wrong or an empty
    /// list when it parses cleanly. Never throws for a bad script, so a caller that expects
    /// invalid input to be normal (an editor validating as the user types) does not pay for an
    /// exception per keystroke. Prefer <see cref="FishboneProgram.ParseSource"/> when the parsed
    /// program is actually wanted; this discards it.
    /// </summary>
    /// <summary>
    /// True when <paramref name="sourceCode"/> is nested or chained deeply enough that parsing it
    /// risks a <see cref="StackOverflowException"/>, which cannot be caught and takes the process
    /// with it. A caller that can defer, such as an editor validating in the background, should
    /// skip the parse; a caller acting on an explicit request should carry on, since refusing
    /// would be a worse answer than the risk. Cheap: a single pass over the characters.
    /// </summary>
    public static bool IsTooDeepToValidate(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);
        return ParseDepthGuard.LooksTooDeepToParse(sourceCode);
    }

    public static IReadOnlyList<FishboneDiagnostic> Validate(string sourceCode)
    {
        ArgumentNullException.ThrowIfNull(sourceCode);

        try
        {
            return ASTParser.TryParse(sourceCode, out _, out var diagnostics) ? [] : diagnostics;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // a parser bug must not take down a caller that is only validating. From gives back a
            // diagnostic with no location, so a client rendering these has nowhere to point it,
            // which is the right outcome for something the source did not really ask for
            return FishboneDiagnostics.From(exception);
        }
    }
}