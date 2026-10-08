using Fishbone;
using SpineIDE.Services;

namespace SpineIDE.Views.Editor;

/// <summary>
/// The global completion surface: language keywords plus every ambient name a script can see
/// (built-ins, plugin functions, registered types, constants). Built once from
/// <see cref="FishboneConfiguration.Describe"/> on the same configuration a run uses, so it gives
/// both autocomplete and parameter hints without any parsing.
/// </summary>
public sealed class FishboneCompletionCatalog
{
    private static readonly Lazy<FishboneCompletionCatalog> LazyShared = new(Build, isThreadSafe: true);

    /// <summary>The process-wide catalog. First access loads plugins (touches disk); warm it off-thread.</summary>
    public static FishboneCompletionCatalog Shared => LazyShared.Value;

    // grammar keywords (Fishbone.g4)
    private static readonly string[] KeywordNames =
    [
        "let", "func", "if", "else", "while", "foreach", "in", "as",
        "return", "break", "continue", "try", "catch", "finally", "throw",
        "true", "false", "null"
    ];

    private FishboneCompletionCatalog(
        IReadOnlyList<FishboneCompletionItem> globals,
        IReadOnlyDictionary<char, IReadOnlyList<FishboneCompletionItem>> globalsByInitial,
        IReadOnlyList<FishboneCompletionItem> keywords,
        IReadOnlyDictionary<string, IReadOnlyList<FishboneSignature>> signatures,
        FishboneDescription? description)
    {
        Description = description;
        Globals = globals;
        GlobalsByInitial = globalsByInitial;
        Keywords = keywords;
        Signatures = signatures;
    }

    /// <summary>Built-ins, plugin functions, registered types and constants — the ambient API.</summary>
    public IReadOnlyList<FishboneCompletionItem> Globals { get; }

    /// <summary>
    /// <see cref="Globals"/> bucketed by lowercase initial character. With plugins loaded this list
    /// runs into the thousands; the popup only ever needs the bucket matching the typed identifier's
    /// first letter, so handing the window just that bucket keeps per-keystroke filtering cheap.
    /// </summary>
    public IReadOnlyDictionary<char, IReadOnlyList<FishboneCompletionItem>> GlobalsByInitial { get; }

    /// <summary>The configuration's description, for member completion and the analyzer. Null when it couldn't be built.</summary>
    public FishboneDescription? Description { get; }

    /// <summary>Language keywords.</summary>
    public IReadOnlyList<FishboneCompletionItem> Keywords { get; }

    /// <summary>Callable name → its overload signatures, for parameter-hint (signature help) popups.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FishboneSignature>> Signatures { get; }

    private static FishboneCompletionCatalog Build()
    {
        var keywords = KeywordNames
            .Select(k => new FishboneCompletionItem(k, FishboneCompletionKind.Keyword, $"keyword  {k}"))
            .ToArray();

        var globals = new List<FishboneCompletionItem>();
        var signatures = new Dictionary<string, IReadOnlyList<FishboneSignature>>(StringComparer.Ordinal);
        FishboneDescription? description = null;
        try
        {
            var configuration = SpineConfiguration.Create(_ => { }, _ => { }, () => string.Empty);
            description = configuration.Describe();
            foreach (var symbol in description.Symbols)
            {
                var (kind, label) = symbol.Kind switch
                {
                    FishboneSymbolKind.Function => (FishboneCompletionKind.Function, "function"),
                    FishboneSymbolKind.Type => (FishboneCompletionKind.Type, "type"),
                    FishboneSymbolKind.Value => (FishboneCompletionKind.Variable, "value"),
                    _ => (FishboneCompletionKind.Constant, "constant")
                };
                var sigs = symbol.Signatures.Select(signature => ToDisplay(symbol.Name, signature)).ToList();

                string tooltip = sigs.Count > 0
                    ? $"{label}  {sigs[0].ToCompactString()}"
                    : $"{label}  {symbol.Name} : {FriendlyType(symbol.Type)}";
                globals.Add(new FishboneCompletionItem(symbol.Name, kind, tooltip));

                if (sigs.Count > 0)
                    signatures[symbol.Name] = sigs;
            }
        }
        catch
        {
        }

        var globalsByInitial = globals
            .Where(g => g.Text.Length > 0)
            .GroupBy(g => char.ToLowerInvariant(g.Text[0]))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FishboneCompletionItem>)group.ToList());

        return new FishboneCompletionCatalog(globals, globalsByInitial, keywords, signatures, description);
    }

    /// <summary>A signature from <see cref="FishboneConfiguration.Describe"/> in the form the editor shows.</summary>
    public static FishboneSignature ToDisplay(string name, Fishbone.FishboneSignature signature) => new(
        name,
        signature.Parameters
            .Select(parameter => new FishboneParameter(parameter.Name, FriendlyType(parameter.Type), parameter.Direction switch
            {
                ParameterDirection.Out => FishboneParamDirection.Out,
                ParameterDirection.Ref => FishboneParamDirection.Ref,
                _ => FishboneParamDirection.In
            }))
            .ToList(),
        FriendlyType(signature.ReturnType));

    // the keyword names (int, string, ...) where there is one, and generic names without `1
    private static string FriendlyType(Type type)
    {
        if (type == typeof(void))
            return "void";
        var keyword = ReservedTypes.PrimitiveTypeNames.FirstOrDefault(entry => entry.Value == type).Key;
        if (keyword is not null)
            return keyword;
        int tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }
}
