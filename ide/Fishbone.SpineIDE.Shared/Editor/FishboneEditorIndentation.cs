namespace SpineIDE.Views.Editor;

public static class FishboneEditorIndentation
{
    public static string IndentForNewLine(string previousLine, int indentationSize)
    {
        int length = 0;
        while (length < previousLine.Length && char.IsWhiteSpace(previousLine[length]) && previousLine[length] != '\r' && previousLine[length] != '\n')
            length++;

        var indent = previousLine[..length];
        return RemoveStringsAndComments(previousLine).TrimEnd().EndsWith('{')
            ? indent + new string(' ', indentationSize)
            : indent;
    }

    public static bool ShouldDedentClosingBrace(string lineBeforeBrace)
    {
        return string.IsNullOrWhiteSpace(lineBeforeBrace);
    }

    private static string RemoveStringsAndComments(string line)
    {
        var result = new char[line.Length];
        bool inString = false;
        bool inBlockComment = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];
            char next = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inString)
            {
                result[i] = ' ';
                if (current == '\\' && next != '\0')
                    result[++i] = ' ';
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (inBlockComment)
            {
                result[i] = ' ';
                if (current == '*' && next == '/')
                {
                    result[++i] = ' ';
                    inBlockComment = false;
                }
                continue;
            }

            if (current == '/' && next == '/')
                break;

            if (current == '/' && next == '*')
            {
                result[i] = ' ';
                result[++i] = ' ';
                inBlockComment = true;
                continue;
            }

            if (current == '"')
            {
                result[i] = ' ';
                inString = true;
                continue;
            }

            result[i] = current;
        }

        return new string(result);
    }
}