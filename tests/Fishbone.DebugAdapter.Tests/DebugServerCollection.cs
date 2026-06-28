namespace Fishbone.DebugAdapter.Tests;

/// <summary>
/// Groups the tests that each spin up a real TCP/DAP server. Marking the collection
/// non-parallelizable keeps these heavyweight, timing-sensitive tests from competing with one
/// another (and starving the DAP handshake) when the whole suite runs.
/// </summary>
[CollectionDefinition("DebugServer", DisableParallelization = true)]
public sealed class DebugServerCollection
{
}