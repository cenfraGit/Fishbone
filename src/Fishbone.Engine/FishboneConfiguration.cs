using System.Collections;

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
        RegisterDefaultCollections();
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

    public void RegisterDefaultCollections()
    {
        BuiltIns["List"] = new Func<IList>(() => new List<object>());
        BuiltIns["Dict"] = new Func<IDictionary>(() => new Dictionary<string, object>(StringComparer.Ordinal));
        BuiltIns["addToList"] = new Action<IList, object>((targetList, item) =>
        {
            if (targetList == null) throw new ArgumentNullException(nameof(targetList));
            targetList.Add(item);
        });
        BuiltIns["addToDict"] = new Action<IDictionary, string, object>((targetDict, key, value) =>
        {
            if (targetDict == null) throw new ArgumentNullException(nameof(targetDict));
            targetDict[key] = value;
        });
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

        BuiltIns["getMember"] = new Func<object, string, object?>((obj, memberName) =>
        {
            if (obj is null) return null;
            var type = obj.GetType();

            var prop = type.GetProperty(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop is not null) return prop.GetValue(obj);

            var field = type.GetField(memberName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field is not null) return field.GetValue(obj);

            throw new Exception($"Type \"{type.Name}\" does not have a public property or field named \"{memberName}\".");
        });

        BuiltIns["getIndex"] = new Func<object, int, object?>((collection, index) =>
        {
            if (collection is IList list)
                return list[index];
            throw new Exception($"Object is not an indexable collection.");
        });

        BuiltIns["setIndex"] = new Action<IList, int, object>((targetList, index, value) =>
        {
            if (targetList == null) throw new ArgumentNullException(nameof(targetList));
            targetList[index] = value;
        });

        BuiltIns["getKey"] = new Func<object, object, object?>((dictionary, key) =>
        {
            if (dictionary is IDictionary dict)
                return dict[key];
            throw new Exception($"Object is not a dictionary.");
        });
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
}