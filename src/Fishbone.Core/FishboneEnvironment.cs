namespace Fishbone.Core;

public class FishboneEnvironment
{
    private readonly FishboneEnvironment? _parent;
    private readonly Dictionary<string, object> _values = [];
    private readonly Dictionary<string, object> _builtins = [];

    public Dictionary<string, object> Values { get => _values; }
    public FishboneEnvironment? Parent => _parent;
    public IReadOnlyDictionary<string, object> LocalValues => _values;

    public TextReader StdIn { get; set; } = Console.In;
    public TextWriter StdOut { get; set; } = Console.Out;

    public FishboneEnvironment(FishboneEnvironment? parent = null)
    {
        _parent = parent;
    }

    public void AddBuiltIn(string name, object value)
    {
        _builtins[name] = value;
    }

    public void Declare(string name, object value)
    {
        if (_values.ContainsKey(name))
            throw new Exception($"Variable \"{name}\" is already declared.");

        _values[name] = value;
    }

    public void Assign(string name, object value)
    {
        if (_values.ContainsKey(name))
        {
            _values[name] = value;
            return;
        }

        if (_parent != null)
        {
            _parent.Assign(name, value);
            return;
        }

        throw new Exception($"Undefined variable \"{name}\".");
    }

    public object GetValue(string name)
    {
        if (_values.TryGetValue(name, out var val))
            return val;
        if (_builtins.TryGetValue(name, out var builtin))
            return builtin;
        if (_parent != null)
            return _parent.GetValue(name);
        throw new Exception($"Undefined variable \"{name}\".");
    }
}