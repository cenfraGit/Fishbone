// --------------------------------------------------------------------------------
// FishboneConfigurationException.cs
//
// a configuration couldn't be set up, like a plugin pointed at a folder that doesn't
// exist or bringing a name that's already taken. it happens while the host builds the
// configuration, before any script runs.
// --------------------------------------------------------------------------------

namespace Fishbone;

public class FishboneConfigurationException : Exception
{
    /// <summary>The names that clashed, when that's what went wrong. Empty otherwise.</summary>
    public IReadOnlyList<string> Names { get; }

    public FishboneConfigurationException(string message, IReadOnlyList<string>? names = null, Exception? inner = null)
        : base(message, inner)
    {
        Names = names ?? [];
    }
}
