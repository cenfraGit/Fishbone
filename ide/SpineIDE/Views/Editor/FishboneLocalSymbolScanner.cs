using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SpineIDE.Views.Editor;

/// <summary>
/// A lightweight, regex-based collector of script-local names (<c>let</c> variables, <c>func</c>
/// declarations and their parameters).
/// </summary>
public static partial class FishboneLocalSymbolScanner
{
    [GeneratedRegex(@"\blet\s+([A-Za-z_]\w*)", RegexOptions.Compiled)]
    private static partial Regex LetDeclaration();

    [GeneratedRegex(@"\bfunc\s+([A-Za-z_]\w*)\s*\(([^)]*)\)", RegexOptions.Compiled)]
    private static partial Regex FuncDeclaration();

    [GeneratedRegex(@"^([A-Za-z_]\w*)", RegexOptions.Compiled)]
    private static partial Regex LeadingIdentifier();

    public static IEnumerable<FishboneCompletionData> Scan(string textBeforeCaret)
    {
        var seen = new HashSet<string>();

        foreach (Match match in LetDeclaration().Matches(textBeforeCaret))
        {
            string name = match.Groups[1].Value;
            if (seen.Add(name))
                yield return new FishboneCompletionData(name, FishboneCompletionKind.Variable, $"variable  {name}");
        }

        foreach (Match match in FuncDeclaration().Matches(textBeforeCaret))
        {
            string funcName = match.Groups[1].Value;
            if (seen.Add(funcName))
                yield return new FishboneCompletionData(funcName, FishboneCompletionKind.Function, $"function  {funcName}(...)");

            foreach (string rawParam in match.Groups[2].Value.Split(','))
            {
                Match paramMatch = LeadingIdentifier().Match(rawParam.Trim());
                if (paramMatch.Success && seen.Add(paramMatch.Groups[1].Value))
                    yield return new FishboneCompletionData(paramMatch.Groups[1].Value, FishboneCompletionKind.Parameter, $"parameter  {paramMatch.Groups[1].Value}");
            }
        }
    }
}