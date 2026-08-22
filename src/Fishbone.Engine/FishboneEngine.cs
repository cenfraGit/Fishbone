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
}