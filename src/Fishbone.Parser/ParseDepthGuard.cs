// --------------------------------------------------------------------------------
// ParseDepthGuard.cs
//
// a cheap look at the source before parsing it, to refuse input deep enough to
// overflow the stack.
//
// the parser, the ast builder, and the interpreter all recurse over the expression
// rule, so deeply nested input blows the stack, and a StackOverflowException cannot
// be caught: it takes the process down with whatever was unsaved in it.
//
// parsing now happens on an enlarged stack (see DeepStackRunner) and copes with about
// 2500 chained binary operators. evaluation does not: it runs on whatever thread the
// host called Run from, and dies between 400 and 500 in a debug build, around 900 in
// release. so evaluation is what sets the limit here, even though the check happens
// before parsing, because the source text is the only place a cheap check can look.
//
// the check is deliberately crude and conservative: it counts characters rather than
// understanding them. refusing costs a diagnostic; not refusing costs the process.
// --------------------------------------------------------------------------------

namespace Fishbone.Parser;

public static class ParseDepthGuard
{
    /// <summary>
    /// The depth at which input is refused. Set from the measured ceiling of the interpreter,
    /// not the parser: parsing on the enlarged stack copes with 2500 chained operators, but
    /// evaluating the resulting tree runs on the caller's ordinary stack and dies between 400
    /// and 500 in a debug build (around 900 in release). 250 keeps most of a factor of two
    /// against the worse of those.
    /// </summary>
    public const int DepthLimit = 250;

    /// <summary>
    /// True when <paramref name="source"/> is nested or chained deeply enough that parsing it
    /// risks a stack overflow. A caller that can defer (an editor validating in the background)
    /// should skip the parse; a caller acting on an explicit request should carry on, since
    /// refusing would be a worse answer than the risk.
    /// </summary>
    public static bool LooksTooDeepToParse(string source) => ExceedsDepth(source, DepthLimit);

    private static bool ExceedsDepth(string source, int limit)
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
                    if (++depth > limit) return true;
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
                    if (++operators > limit) return true;
                    continue;
            }
        }

        return false;
    }
}