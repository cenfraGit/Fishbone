namespace Fishbone.Engine;

public class FishboneConfiguration
{
    public bool EnableImports { get; set; } = true;
    public bool EnableLoops { get; set; } = true;
    public bool EnableFunctionDeclaration { get; set; } = true;
    public bool EnableFunctionCall { get; set; } = true;

    // engine globals (for seeding engine with values or delegates)
    public Dictionary<string, object> Globals { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Register C# delegates as Fishbone callables.
    /// </summary>
    public FishboneConfiguration RegisterFunction(string name, Delegate csharpMethod)
    {
        Globals[name] = csharpMethod;
        return this;
    }

    /// <summary>
    /// Register a value that will be accessible from within script.
    /// </summary>
    public FishboneConfiguration RegisterVariable(string name, object value)
    {
        Globals[name] = value;
        return this;
    }
}