using Fishbone.Interpreter;
using HalconDotNet;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Fishbone.Plugins.Halcon24111;

public partial class HalconOperatorPlugin : IFishbonePlugin
{
    // HOperatorSet's own entry point, which is not a HALCON operator
    private static readonly HashSet<string> ExcludedMethods = ["Main"];

    public void Register(FishboneConfiguration config)
    {
        config.AddTypeConverter(
            typeof(HTuple),
            toNet: value => HalconConverters.ToHTuple(value),
            fromNet: value => HalconConverters.FromHTuple((HTuple)value));

        var methods = typeof(HOperatorSet)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => !ShouldExclude(method));

        // group overloads under one script name so a call resolves across all of them, rather than
        // the last registration silently winning
        foreach (var overloads in methods.GroupBy(method => ToSnakeCase(method.Name)))
            config.AddBuiltIn(overloads.Key, new BoundMethod(target: null, overloads.ToArray()));
    }

    private static bool ShouldExclude(MethodInfo method)
    {
        if (ExcludedMethods.Contains(method.Name))
            return true;

        if (method.Name.StartsWith("Internal", StringComparison.Ordinal))
            return true;

        return false;
    }

    public static string ToSnakeCase(string pascal)
    {
        var result = AcronymPattern().Replace(pascal, "$1_$2");
        result = WordBoundaryPattern().Replace(result, "$1_$2");
        return result.ToLowerInvariant();
    }

    [GeneratedRegex(@"([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymPattern();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryPattern();
}