using Fishbone.Interpreter;

namespace Fishbone.Engine;

public class FishboneConfiguration
{
    public bool EnableImports { get; set; } = true;
    public bool EnableLoops { get; set; } = true;
    public bool EnableFunctionDeclaration { get; set; } = true;
    public bool EnableFunctionCall { get; set; } = true;

    public Dictionary<string, object> BuiltIns { get; } = new(StringComparer.Ordinal);

    public FishboneConfiguration(bool injectDefaults = true)
    {
        if (injectDefaults)
            RegisterDefaults();
    }

    public void RegisterDefaults()
    {
        RegisterDefaultConstants();
        RegisterDefaultIO();
        RegisterDefaultMath();
        RegisterDefaultReflection();
    }

    public void RegisterDefaultConstants()
    {
        BuiltIns["PI"] = Math.PI;
        BuiltIns["E"] = Math.E;
    }

    public void RegisterDefaultIO()
    {
        BuiltIns["print"] = new Action<object?>(value => Console.Write(value?.ToString()));
        BuiltIns["println"] = new Action<object?>(value => Console.WriteLine(value?.ToString()));
        BuiltIns["input"] = new Func<string>(() => Console.ReadLine() ?? string.Empty);
    }

    public void RegisterDefaultMath()
    {
        BuiltIns["abs"] = new Func<double, double>(Math.Abs);
        BuiltIns["round"] = new Func<double, int, double>(Math.Round);
        BuiltIns["min"] = new Func<double, double, double>(Math.Min);
        BuiltIns["max"] = new Func<double, double, double>(Math.Max);
        BuiltIns["pow"] = new Func<double, double, double>(Math.Pow);
        BuiltIns["sqrt"] = new Func<double, double>(Math.Sqrt);
    }

    public void RegisterDefaultReflection()
    {
        BuiltIns["int"] = new Func<object?, int>(value =>
        {
            if (value is null) return 0;
            if (value is string str)
            {
                if (double.TryParse(str, out double strDouble))
                    return (int)strDouble;
                return int.TryParse(str, out int strInt) ? strInt : 0;
            }
            try
            {
                return (int)Convert.ChangeType(value, typeof(int));
            }
            catch
            {
                return 0;
            }
        });

        BuiltIns["double"] = new Func<object?, double>(value =>
        {
            if (value is null) return 0.0;
            if (value is string str)
                if (double.TryParse(str, out double strDouble))
                    return strDouble;

            try
            {
                return (double)Convert.ChangeType(value, typeof(double));
            }
            catch
            {
                return 0.0;
            }
        });

        BuiltIns["string"] = new Func<object?, string>(value => value?.ToString() ?? string.Empty);
    }

    public FishboneConfiguration RegisterBuiltIn(string name, object value)
    {
        BuiltIns[name] = value;
        return this;
    }

    public FishboneConfiguration RegisterFunction(string name, Delegate csharpMethod)
    {
        BuiltIns[name] = csharpMethod;
        return this;
    }

    /// <summary>
    /// Registers a .NET type so scripts can construct it by calling its name like a function,
    /// for example <c>let p = Point(1, 2);</c>. The script-visible name defaults to the type's
    /// short name; pass <paramref name="name"/> to override it.
    /// </summary>
    public FishboneConfiguration RegisterType<T>(string? name = null) =>
        RegisterType(typeof(T), name);

    public FishboneConfiguration RegisterType(Type type, string? name = null)
    {
        BuiltIns[name ?? type.Name] = new RegisteredType(type);
        return this;
    }
}