using System;

namespace SpineIDE.Views.Editor;

public static class FishboneEditorIndentation
{
    public static string IndentUnit(int indentationSize) => new(' ', indentationSize);

    public static string LeadingWhitespace(string text)
    {
        int length = 0;
        while (length < text.Length && char.IsWhiteSpace(text[length]) && text[length] != '\r' && text[length] != '\n')
            length++;

        return text[..length];
    }

    public static string IndentForNewLine(string previousLine, int indentationSize)
    {
        var indent = LeadingWhitespace(previousLine);
        return EndsWithOpenBrace(previousLine)
            ? indent + IndentUnit(indentationSize)
            : indent;
    }

    public static string IndentForNewLine(string documentText, int lineStartOffset, int caretOffset, int indentationSize)
    {
        string lineBeforeCaret = documentText[lineStartOffset..caretOffset];
        var indent = LeadingWhitespace(lineBeforeCaret);
        return EndsWithOpenBrace(documentText, lineStartOffset, caretOffset)
            ? indent + IndentUnit(indentationSize)
            : indent;
    }

    public static string Dedent(string indent, int indentationSize)
    {
        int removeCount = Math.Min(indentationSize, indent.Length);
        return indent[..^removeCount];
    }

    public static bool ShouldDedentClosingBrace(string lineBeforeBrace)
    {
        return string.IsNullOrWhiteSpace(lineBeforeBrace);
    }

    public static bool IsBetweenBraces(string text, int caretOffset)
    {
        return caretOffset > 0
            && caretOffset < text.Length
            && text[caretOffset - 1] == '{'
            && text[caretOffset] == '}';
    }

    public static bool EndsWithOpenBrace(string line)
    {
        var meaningful = RemoveStringsAndComments(line).TrimEnd();
        return meaningful.EndsWith('{');
    }

    public static bool EndsWithOpenBrace(string documentText, int lineStartOffset, int caretOffset)
    {
        bool startsInBlockComment = IsInBlockComment(documentText, lineStartOffset);
        var meaningful = RemoveStringsAndComments(documentText[lineStartOffset..caretOffset], startsInBlockComment).TrimEnd();
        return meaningful.EndsWith('{');
    }

    private static bool IsInBlockComment(string text, int offset)
    {
        bool inString = false;
        bool inBlockComment = false;

        for (int i = 0; i < offset; i++)
        {
            char current = text[i];
            char next = i + 1 < offset ? text[i + 1] : '\0';

            if (inString)
            {
                if (current == '\\' && next != '\0')
                    i++;
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (current == '/' && next == '/')
            {
                while (i < offset && text[i] != '\r' && text[i] != '\n')
                    i++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (current == '"')
                inString = true;
        }

        return inBlockComment;
    }

    private static string RemoveStringsAndComments(string line, bool startsInBlockComment = false)
    {
        var result = new char[line.Length];
        bool inString = false;
        bool inBlockComment = startsInBlockComment;

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