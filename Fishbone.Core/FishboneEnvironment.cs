namespace Fishbone.Core;

public class FishboneEnvironment
{
    private readonly Dictionary<string, object> _values = [];
    public Dictionary<string, object> Values { get => _values; }

    public FishboneEnvironment()
    {
    }

    public void Declare(string name, object value)
    {
        if (_values.ContainsKey(name))
            throw new Exception($"Variable \"{name}\" is already declared.");

        _values[name] = value;
    }

    public void Assign(string name, object value)
    {
        if (!_values.ContainsKey(name))
            throw new Exception($"Undefined variable \"{name}\"");

        _values[name] = value;
    }

    public object GetValue(string name)
    {
        if (!_values.ContainsKey(name))
            throw new Exception($"Undefined variable \"{name}\"");

        return _values[name];
    }
}