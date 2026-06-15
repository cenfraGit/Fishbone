using System.Collections;
using System.Reflection;

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

        // temporary: interfacing with c# types (no grammar for these yet)

        // for creating lists
        _values["List"] = new Func<IList>(() => new List<object>());
        // for creating maps
        _values["Dict"] = new Func<IDictionary>(() => new Dictionary<string, object>(StringComparer.Ordinal));

        // add to list
        _values["addToList"] = new Action<IList, object>((targetList, item) =>
        {
            if (targetList == null) throw new ArgumentNullException(nameof(targetList));
            targetList.Add(item);
        });

        // add to dictionary
        _values["addToDict"] = new Action<IDictionary, string, object>((targetDict, key, value) =>
        {
            if (targetDict == null) throw new ArgumentNullException(nameof(targetDict));
            targetDict[key] = value;
        });

        // for object properties and fields
        _values["getMember"] = new Func<object, string, object?>((obj, memberName) =>
        {
            if (obj is null) return null;
            var type = obj.GetType();

            // check if property
            var prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null) return prop.GetValue(obj);

            // check if field
            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null) return field.GetValue(obj);

            throw new Exception($"Type \"{type.Name}\" does not have a public property or field named \"{memberName}\".");
        });

        // for arrays/list indexing
        _values["getIndex"] = new Func<object, int, object?>((collection, index) =>
        {
            if (collection is System.Collections.IList list)
                return list[index];
            throw new Exception($"Object is not an indexable collection.");
        });

        _values["setIndex"] = new Action<IList, int, object>((targetList, index, value) =>
        {
            if (targetList == null) throw new ArgumentNullException(nameof(targetList));
            targetList[index] = value;
        });

        _values["getKey"] = new Func<object, object, object?>((dictionary, key) =>
        {
            if (dictionary is System.Collections.IDictionary dict)
                return dict[key];
                throw new Exception($"Object is not a dictionary.");
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