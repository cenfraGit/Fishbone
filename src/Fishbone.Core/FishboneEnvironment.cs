namespace Fishbone.Core;

public class FishboneEnvironment
{
    private readonly FishboneEnvironment? _parent;
    private readonly Dictionary<string, object> _values = [];
    public Dictionary<string, object> Values { get => _values; }

    public TextWriter StdOut { get; set; } = Console.Out;

    public FishboneEnvironment(FishboneEnvironment? parent = null)
    {
        _parent = parent;

        if (parent is null)
            RegisterCoreLibraries();
    }

    private void RegisterCoreLibraries()
    {
        _values["print"] = new Action<object?>(value =>
        {
            StdOut.WriteLine(value?.ToString());
        });
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
        if (_values.TryGetValue(name, out var val)) return val;
        if (_parent != null) return _parent.GetValue(name);
        throw new Exception($"Undefined variable \"{name}\".");
    }
}