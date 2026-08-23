// --------------------------------------------------------------------------------
// RuntimeIdentifiers.cs
//
// which platform folders to look in, in order.
//
// nuget has a full runtime graph (runtime.json) describing that win10-x64 falls back to
// win81-x64, then win8-x64, then win-x64, then win, then any. loading that graph means
// restoring Microsoft.NETCore.Platforms and parsing it, which is a lot of machinery for
// choosing a folder out of a plugin package.
//
// the packages this has to handle publish exactly two shapes: an exact rid such as
// win-x64, or a bare platform such as win. so the chain here is the specific rid, then
// the platform without its architecture, then any. if a package ever needs more than
// that, the real graph is the answer rather than a longer list here.
// --------------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace Fishbone.Hosting.NuGet;

internal static class RuntimeIdentifiers
{
    /// <summary>
    /// The identifier for the process this is running in, for example <c>win-x64</c>. Built from
    /// the running platform rather than read from the build, so a portable build installs assets
    /// for the machine it is actually on.
    /// </summary>
    public static string Current
    {
        get
        {
            string platform =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                "any";

            string architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => string.Empty,
            };

            return architecture.Length == 0 ? platform : $"{platform}-{architecture}";
        }
    }

    /// <summary>
    /// The folders to try for <paramref name="runtimeIdentifier"/>, most specific first. Never
    /// empty, and never yields the same value twice.
    /// </summary>
    public static IEnumerable<string> FallbackChain(string runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier))
        {
            yield return "any";
            yield break;
        }

        yield return runtimeIdentifier;

        int dash = runtimeIdentifier.IndexOf('-');
        if (dash > 0)
            yield return runtimeIdentifier[..dash];

        if (runtimeIdentifier != "any")
            yield return "any";
    }
}
