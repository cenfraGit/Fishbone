using Fishbone;
using Fishbone.DebugAdapter;
using Fishbone.Plugins.Math;

// the engine and a plugin from their packages, and the debug adapter's types loading
var config = new FishboneConfiguration().AddPlugin(new MathPlugin());
var env = FishboneProgram.Run("let result = sqrt(16) + 1;", config);
Console.WriteLine($"result {env.GetValue("result")}");
Console.WriteLine($"adapter {typeof(FishboneDebugServer).Assembly.GetName().Name}");
