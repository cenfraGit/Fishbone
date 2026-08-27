// --------------------------------------------------------------------------------
// MathPlugin.cs
//
// adds basic math functionality to a fishbone script.
// --------------------------------------------------------------------------------

namespace Fishbone.Plugins.Math;

public sealed class MathPlugin : IFishbonePlugin
{
    public void Register(FishboneConfiguration config)
    {
        // constants
        config.BuiltIns["PI"] = System.Math.PI;
        config.BuiltIns["E"] = System.Math.E;

        config.BuiltIns["abs"] = new Func<double, double>(System.Math.Abs);
        config.BuiltIns["round"] = new Func<double, int, double>(System.Math.Round);
        config.BuiltIns["min"] = new Func<double, double, double>(System.Math.Min);
        config.BuiltIns["max"] = new Func<double, double, double>(System.Math.Max);
        config.BuiltIns["pow"] = new Func<double, double, double>(System.Math.Pow);
        config.BuiltIns["sqrt"] = new Func<double, double>(System.Math.Sqrt);
    }
}