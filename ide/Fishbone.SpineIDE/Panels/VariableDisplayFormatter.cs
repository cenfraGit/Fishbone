using System.Collections;
using System.Collections.Generic;
using Fishbone.Debugging;

namespace SpineIDE.Panels;

public sealed class VariableDetailItem
{
    public string Path { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public int Depth { get; init; }
    public string PathDisplay => new string(' ', Depth * 2) + Path;
}

public static class VariableDisplayFormatter
{
    public static bool IsCollection(object? value) => value is IList or IDictionary;

    public static string FormatType(object? value) => DebugValueFormatter.FormatType(value);

    public static string FormatValue(object? value) => DebugValueFormatter.FormatValue(value);

    public static IReadOnlyList<VariableDetailItem> BuildDetailRows(string name, object? value)
    {
        var rows = new List<VariableDetailItem>();
        AddDetailRows(rows, string.IsNullOrWhiteSpace(name) ? "value" : name, value, 0);
        return rows;
    }

    private static void AddDetailRows(List<VariableDetailItem> rows, string path, object? value, int depth)
    {
        rows.Add(new VariableDetailItem
        {
            Path = path,
            Type = FormatType(value),
            Value = FormatValue(value),
            Depth = depth
        });

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
                AddDetailRows(rows, $"{path}[{i}]", list[i], depth + 1);
            return;
        }

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry item in dictionary)
                AddDetailRows(rows, $"{path}[{DebugValueFormatter.FormatDictionaryKey(item.Key)}]", item.Value, depth + 1);
        }
    }
}
