namespace Fishbone.TestPlugin;

public sealed class GoodPlugin : IFishbonePlugin
{
    public void Register(FishboneConfiguration config) => config.AddBuiltIn("test_good", new Func<int>(() => 7));
}

public sealed class ThrowingPlugin : IFishbonePlugin
{
    public void Register(FishboneConfiguration config) => throw new InvalidOperationException("broken on purpose");
}

public sealed class NeedsArgsPlugin(int value) : IFishbonePlugin
{
    public void Register(FishboneConfiguration config) => config.AddBuiltIn("test_needs_args", value);
}
