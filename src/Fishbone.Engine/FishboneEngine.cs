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