namespace Fishbone.Core;

public class FishboneEnvironment
{
    private readonly FishboneEnvironment? _parent;
    private readonly Dictionary<string, object> _values = [];
    public Dictionary<string, object> Values { get => _values; }

    public TextReader StdIn { get; set; } = Console.In;
    public TextWriter StdOut { get; set; } = Console.Out;

    public FishboneEnvironment(FishboneEnvironment? parent = null, bool injectDefaults = true)
    {
        _parent = parent;

        if (parent is null && injectDefaults)
            RegisterDefaults();
    }

    private void RegisterDefaults()
    {
        // constants
        _values["PI"] = Math.PI;
        _values["E"] = Math.E;

        // input/output
        _values["println"] = new Action<object?>(value => { StdOut.Write(value?.ToString()); }); // print line
        _values["print"] = new Action<object?>(value => { StdOut.WriteLine(value?.ToString()); });
        _values["input"] = new Func<string>(() => StdIn.ReadLine() ?? string.Empty);

        // basic math
        _values["abs"]   = new Func<double, double>(Math.Abs);
        _values["round"] = new Func<double, int, double>(Math.Round);
        _values["min"]   = new Func<double, double, double>(Math.Min);
        _values["max"]   = new Func<double, double, double>(Math.Max);
        _values["pow"]   = new Func<double, double, double>(Math.Pow);
        _values["sqrt"]  = new Func<double, double>(Math.Sqrt);

        // int(someValue)
        _values["int"] = new Func<object?, int>(value =>
        {
            if (value is null) return 0;
            if (value is string str)
            {
                if (double.TryParse(str, out double strDouble))
                    return (int)strDouble;
                return int.TryParse(str, out int strInt) ? strInt : 0;
            }
            // change type
            try
            {
                return (int)Convert.ChangeType(value, typeof(int));
            }
            catch
            {
                return 0;
            }
        });

        // double(someValue)
        _values["double"] = new Func<object?, double>(value =>
        {
            if (value is null) return 0.0;
            if (value is string str)
                if (double.TryParse(str, out double strDouble))
                    return (int)strDouble;

            try
            {
                return (double)Convert.ChangeType(value, typeof(double));
            }
            catch
            {
                return 0.0;
            }
        });

        // string(someValue)
        _values["string"] = new Func<object?, string>(value => value?.ToString() ?? string.Empty);
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