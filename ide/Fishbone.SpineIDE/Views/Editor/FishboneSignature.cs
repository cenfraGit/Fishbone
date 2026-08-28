using System.Collections.Generic;
using System.Linq;

namespace SpineIDE.Views.Editor;

public enum FishboneParamDirection
{
    In,
    Out,
    Ref
}

public sealed record FishboneParameter(string Name, string Type, FishboneParamDirection Direction)
{
    public string DirectionKeyword => Direction switch
    {
        FishboneParamDirection.Out => "out",
        FishboneParamDirection.Ref => "ref",
        _ => "in"
    };
}

/// <summary>One callable signature (a plugin function, built-in, overload, or constructor).</summary>
public sealed record FishboneSignature(string Name, IReadOnlyList<FishboneParameter> Parameters, string? ReturnType)
{
    /// <summary>Compact one-line form for completion tooltips; <c>in</c> is implicit, only out/ref annotated.</summary>
    public string ToCompactString()
    {
        var parameters = Parameters.Select(p =>
            (p.Direction == FishboneParamDirection.In ? string.Empty : p.DirectionKeyword + " ") + $"{p.Type} {p.Name}");
        string call = $"{Name}({string.Join(", ", parameters)})";
        return ReturnType is null ? call : $"{call} : {ReturnType}";
    }
}