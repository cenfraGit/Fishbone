using System.Collections;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

internal static class HalconConverters
{
    public static HTuple ToHTuple(object value)
    {
        if (value is HTuple h) return h;
        if (value is int i) return new HTuple(i);
        if (value is long l) return new HTuple(l);
        if (value is double d) return new HTuple(d);
        if (value is string s) return new HTuple(s);
        if (value is bool b) return new HTuple(b ? 1 : 0);

        if (value is IList list)
        {
            var result = new HTuple();
            foreach (var item in list)
            {
                var element = ToHTuple(item);
                result = result.TupleConcat(element);
            }
            return result;
        }

        return new HTuple(value?.ToString() ?? "");
    }

    public static object FromHTuple(HTuple htuple)
    {
        if (htuple is null || htuple.Length == 0)
            return null!;

        if (htuple.Length == 1)
        {
            return htuple.Type switch
            {
                HTupleType.INTEGER or HTupleType.LONG => htuple.L,
                HTupleType.DOUBLE => htuple.D,
                HTupleType.STRING => htuple.S,
                _ => htuple
            };
        }

        var list = new List<object>();
        for (int i = 0; i < htuple.Length; i++)
        {
            var element = htuple[i];
            list.Add(element.Type switch
            {
                HTupleType.INTEGER or HTupleType.LONG => element.L,
                HTupleType.DOUBLE => element.D,
                HTupleType.STRING => element.S,
                _ => element
            });
        }
        return list;
    }
}