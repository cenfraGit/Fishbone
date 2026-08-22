// --------------------------------------------------------------------------------
// ParseDepthGuard.cs
//
// a cheap look at the source before parsing it, to refuse input deep enough to
// overflow the stack.
//
// both the generated parser and AstBuilderVisitor recurse over the expression rule,
// so deeply nested input blows the stack, and a StackOverflowException cannot be
// caught: it takes the process down with whatever was unsaved in it. measured
// thresholds on a default 1MB stack were roughly 500 chained binary operators
// ('1 + 1 + 1 ...', about 2KB of text), 800 nested parentheses, and somewhere
// between 500 and 1000 nested brackets or blocks. a release build survives about
// twice as deep.
//
// pressing Run on such a file has always crashed. parsing on every typing pause
// makes it something a paste can trigger, so the live path checks first. the check
// is deliberately crude and conservative: it counts characters rather than
// understanding them, and refusing to parse costs nothing but a delayed diagnostic.
// --------------------------------------------------------------------------------

namespace Fishbone.Parser;

public static class ParseDepthGuard
{
    /// <summary>
    /// The nesting depth, and the per-statement operator count, at which
    /// <see cref="LooksTooDeepToParse"/> refuses. Well below the measured overflow
    /// thresholds, and far above anything hand-written.
    /// </summary>
    public const int DepthLimit = 300;

    /// <summary>
    /// True when <paramref name="source"/> is nested or chained deeply enough that parsing it
    /// risks a stack overflow. A caller that can defer (an editor validating in the background)
    /// should skip the parse; a caller acting on an explicit request should carry on, since
    /// refusing would be a worse answer than the risk.
    /// </summary>
    public static bool LooksTooDeepToParse(string source)
    {
        if (string.IsNullOrEmpty(source))
            return false;

        int depth = 0;
        int operators = 0;
        bool inLineComment = false;
        bool inBlockComment = false;
        char quote = '\0';

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            // brackets and operators inside a string or comment are text, not structure
            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '/' when next == '/': inLineComment = true; i++; continue;
                case '/' when next == '*': inBlockComment = true; i++; continue;
                case '"' or '\'': quote = c; continue;

                case '(' or '[' or '{':
                    if (++depth > DepthLimit) return true;
                    continue;
                case ')' or ']' or '}':
                    if (depth > 0) depth--;
                    continue;

                // a statement boundary resets the operator run: the recursion that matters is
                // the depth of one expression tree, not how many operators the file contains
                case ';':
                    operators = 0;
                    continue;

                // the binary operators that build a left-recursive chain. '=' is excluded: an
                // assignment is not a chain, and '==' is caught by the first character anyway
                case '+' or '-' or '*' or '%' or '<' or '>' or '&' or '|' or '^':
                    if (++operators > DepthLimit) return true;
                    continue;
            }
        }

        return false;
    }
}