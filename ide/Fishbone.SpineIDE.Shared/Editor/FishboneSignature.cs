using System.Collections.Generic;
using System.Linq;

namespace SpineIDE.Views.Editor;

public sealed record FishboneParameter(string Name, string Type, Fishbone.ParameterDirection Direction)
{
    public string DirectionKeyword => Direction.ToString().ToLowerInvariant();
}

/// <summary>One callable signature (a plugin function, built-in, overload, or constructor).</summary>
public sealed record FishboneSignature(string Name, IReadOnlyList<FishboneParameter> Parameters, string? ReturnType)
{
    /// <summary>A signature from <see cref="Fishbone.FishboneConfiguration.Describe"/> in the form the editor shows.</summary>
    public static FishboneSignature From(string name, Fishbone.FishboneSignature signature) => new(
        name,
        signature.Parameters
            .Select(parameter => new FishboneParameter(parameter.Name, FriendlyType(parameter.Type), parameter.Direction))
            .ToList(),
        FriendlyType(signature.ReturnType));

    // the keyword names (int, string, ...) where there is one, and generic names without `1
    private static string FriendlyType(System.Type type)
    {
        if (type == typeof(void))
            return "void";
        var keyword = Fishbone.ReservedTypes.PrimitiveTypeNames.FirstOrDefault(entry => entry.Value == type).Key;
        if (keyword is not null)
            return keyword;
        int tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }
}
