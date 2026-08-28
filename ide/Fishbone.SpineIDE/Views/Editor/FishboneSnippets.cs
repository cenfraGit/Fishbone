using System;
using System.Collections.Generic;
using System.Linq;

namespace SpineIDE.Views.Editor;

public sealed record FishboneSnippet(string Header, string Template);

public sealed record PreparedSnippet(string Text, int CaretOffset);

public static class FishboneSnippets
{
    private const string CaretMarker = "$caret$";

    public static IReadOnlyList<FishboneSnippet> Core { get; } =
    [
        new("let", "let $caret$name = value;"),
        new("func", """
func $caret$name()
{

}
"""),
        new("if / else if / else", """
if ($caret$condition)
{

}
else if (otherCondition)
{

}
else
{

}
"""),
        new("while", """
while ($caret$condition)
{

}
"""),
        new("foreach", """
foreach ($caret$value in values)
{

}
"""),
        new("return", "return$caret$;"),
        new("break", "break;$caret$"),
        new("continue", "continue;$caret$"),
        new("list", "[$caret$value]"),
        new("dictionary", """{"$caret$key": value}"""),
        new("import", """import "$caret$file.fb";""")
    ];

    public static PreparedSnippet Prepare(string template, string lineIndent)
    {
        int markerOffset = template.IndexOf(CaretMarker, StringComparison.Ordinal);
        string text = markerOffset >= 0
            ? template.Remove(markerOffset, CaretMarker.Length)
            : template;

        string indentedText = ApplyLineIndent(text, lineIndent);
        int caretOffset = markerOffset >= 0
            ? ApplyLineIndent(template[..markerOffset], lineIndent).Length
            : indentedText.Length;

        return new PreparedSnippet(indentedText, caretOffset);
    }

    private static string ApplyLineIndent(string text, string lineIndent)
    {
        if (string.IsNullOrEmpty(lineIndent))
            return text;

        return text.Replace("\r\n", "\n")
            .Split('\n')
            .Select((line, index) => index == 0 ? line : lineIndent + line)
            .Aggregate((left, right) => left + Environment.NewLine + right);
    }

}