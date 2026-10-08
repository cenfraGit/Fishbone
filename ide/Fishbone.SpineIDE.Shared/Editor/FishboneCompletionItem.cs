namespace SpineIDE.Views.Editor;

/// <summary>What a completion item represents; also drives sort priority in the popup.</summary>
public enum FishboneCompletionKind
{
    Keyword,
    Function,
    Type,
    Constant,
    Variable,
    Parameter
}

/// <summary>
/// A single entry in the editor's completion popup. Plain data with no UI types, so every front end
/// can show it and the catalog can share one instance across editors.
/// </summary>
/// <param name="Description">Shown as the hover tooltip (signature or kind).</param>
public sealed record FishboneCompletionItem(string Text, FishboneCompletionKind Kind, string Description)
{
    // in-scope locals rank above the global API, which ranks above bare keywords
    public double Priority => Kind switch
    {
        FishboneCompletionKind.Variable or FishboneCompletionKind.Parameter => 3,
        FishboneCompletionKind.Function or FishboneCompletionKind.Type or FishboneCompletionKind.Constant => 2,
        _ => 1
    };
}
