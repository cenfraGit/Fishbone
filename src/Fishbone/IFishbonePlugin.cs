// --------------------------------------------------------------------------------
// IFishbonePlugin.cs
//
// a plugin is a way to extend a FishboneConfiguration object with more
// built-ins, values, type converters, etc. it itself doesn't hold a
// FishboneConfiguration; it only has "instructions" on what to add
// (that "what" being built-ins or values, for example) to whatever
// FishboneConfiguration object its passed to the "Register" method
// --------------------------------------------------------------------------------

namespace Fishbone;

public interface IFishbonePlugin
{
    void Register(FishboneConfiguration config);
}