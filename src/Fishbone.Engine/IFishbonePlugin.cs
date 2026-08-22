// --------------------------------------------------------------------------------
// IFishbonePlugin.cs
//
// plugins must register individual configuration objects that represent a plugin.
//
// in the end, fishbone plugins consist of FishboneConfiguration objects that can
// be loaded dynamically using plugin architecture.
// --------------------------------------------------------------------------------

namespace Fishbone.Engine;

public interface IFishbonePlugin
{
    void Register(FishboneConfiguration config);
}