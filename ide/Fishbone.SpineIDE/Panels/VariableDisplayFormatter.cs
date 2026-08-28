using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
    private const int InlineMaxDepth = 2;
    private const int InlineMaxItems = 5;
    private const int ScalarMaxLength = 48;

    public static bool IsCollection(object? value) => value is IList or IDictionary;

    public static string FormatType(object? value)
    {
        if (value is null)
            return "null";

        if (value is IList)
            return "List";

        if (value is IDictionary)
            return "Dictionary";

        string name = value.GetType().Name;
        int genericMarker = name.IndexOf('`');
        return genericMarker >= 0 ? name[..genericMarker] : name;
    }

    public static string FormatValue(object? value)
    {
        return FormatValue(value, 0, quoteStrings: false);
    }

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
                AddDetailRows(rows, $"{path}[{FormatDictionaryPathKey(item.Key)}]", item.Value, depth + 1);
        }
    }

    private static string FormatValue(object? value, int depth, bool quoteStrings)
    {
        if (value is null)
            return "null";

        if (value is string text)
            return quoteStrings ? QuoteString(text) : text;

        if (value is bool boolean)
            return boolean ? "true" : "false";

        if (value is IList list)
            return FormatList(list, depth);

        if (value is IDictionary dictionary)
            return FormatDictionary(dictionary, depth);

        if (value is IFormattable formattable)
            return Truncate(formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty);

        return Truncate(value.ToString() ?? string.Empty);
    }

    private static string FormatList(IList list, int depth)
    {
        if (depth >= InlineMaxDepth)
            return "[...]";

        var parts = new List<string>();
        int visibleCount = Math.Min(list.Count, InlineMaxItems);
        for (int i = 0; i < visibleCount; i++)
            parts.Add(FormatValue(list[i], depth + 1, quoteStrings: true));

        if (list.Count > InlineMaxItems)
            parts.Add("...");

        return "[" + string.Join(", ", parts) + "]";
    }

    private static string FormatDictionary(IDictionary dictionary, int depth)
    {
        if (depth >= InlineMaxDepth)
            return "{...}";

        var parts = new List<string>();
        int count = 0;
        foreach (DictionaryEntry item in dictionary)
        {
            if (count >= InlineMaxItems)
            {
                parts.Add("...");
                break;
            }

            parts.Add($"{FormatDictionaryKey(item.Key)}: {FormatValue(item.Value, depth + 1, quoteStrings: true)}");
            count++;
        }

        return "{" + string.Join(", ", parts) + "}";
    }

    private static string FormatDictionaryKey(object? key)
    {
        return key is string text
            ? QuoteString(text)
            : FormatValue(key, InlineMaxDepth, quoteStrings: true);
    }

    private static string FormatDictionaryPathKey(object? key)
    {
        return key is string text
            ? QuoteString(text)
            : FormatValue(key, InlineMaxDepth, quoteStrings: true);
    }

    private static string Truncate(string value)
    {
        var firstLine = value.AsSpan();
        int newline = firstLine.IndexOfAny('\r', '\n');
        if (newline >= 0)
            firstLine = firstLine[..newline];

        return firstLine.Length <= ScalarMaxLength
            ? firstLine.ToString()
            : string.Concat(firstLine[..ScalarMaxLength], "…");
    }

    private static string QuoteString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char c in value)
        {
            builder.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => c
            });
        }
        builder.Append('"');
        return builder.ToString();
    }
}