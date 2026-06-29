using System.Collections;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv;

internal static class OpenCvConverters
{
    public static InputArray ToInputArray(object value) => (Mat)value;

    public static OutputArray ToOutputArray(object value) => (Mat)value;

    public static InputOutputArray ToInputOutputArray(object value) => (Mat)value;

    // these return object (boxing the struct) so the methods match Func<object, object> directly
    public static object ToSize(object value)
    {
        var parts = ToInts(value);
        return parts.Count switch
        {
            >= 2 => new Size(parts[0], parts[1]),
            1 => new Size(parts[0], parts[0]),   // a single value is a square
            _ => throw new ArgumentException("Size expects a list of [width, height].")
        };
    }

    public static object ToPoint(object value)
    {
        var parts = ToInts(value);
        if (parts.Count < 2)
            throw new ArgumentException("Point expects a list of [x, y].");
        return new Point(parts[0], parts[1]);
    }

    public static object ToScalar(object value)
    {
        var parts = ToDoubles(value);
        return parts.Count switch
        {
            1 => new Scalar(parts[0]),
            2 => new Scalar(parts[0], parts[1]),
            3 => new Scalar(parts[0], parts[1], parts[2]),
            >= 4 => new Scalar(parts[0], parts[1], parts[2], parts[3]),
            _ => throw new ArgumentException("Scalar expects a list of 1 to 4 numbers.")
        };
    }

    private static List<int> ToInts(object value)
    {
        var result = new List<int>();
        foreach (var item in AsEnumerable(value))
            result.Add(Convert.ToInt32(item));
        return result;
    }

    private static List<double> ToDoubles(object value)
    {
        var result = new List<double>();
        foreach (var item in AsEnumerable(value))
            result.Add(Convert.ToDouble(item));
        return result;
    }

    private static IEnumerable AsEnumerable(object value) =>
        value is IEnumerable enumerable and not string
            ? enumerable
            : throw new ArgumentException($"Expected a list but got {value?.GetType().Name ?? "null"}.");
}