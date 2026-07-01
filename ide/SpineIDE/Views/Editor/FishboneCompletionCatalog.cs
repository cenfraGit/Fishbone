using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fishbone.Engine;
using Fishbone.Interpreter;

namespace SpineIDE.Views.Editor;

/// <summary>
/// The global completion surface: language keywords plus every ambient name a script can see
/// (built-ins, plugin functions, registered types, constants). Built once by running the plugin
/// loader into a throwaway <see cref="FishboneConfiguration"/> and projecting its dictionaries into
/// completion items and structured signatures, get both autocomplete and parameter hints without 
/// any parsing.
/// </summary>
public sealed class FishboneCompletionCatalog
{
    private static readonly Lazy<FishboneCompletionCatalog> LazyShared = new(Build, isThreadSafe: true);

    /// <summary>The process-wide catalog. First access loads plugins (touches disk); warm it off-thread.</summary>
    public static FishboneCompletionCatalog Shared => LazyShared.Value;

    // grammar keywords (Fishbone.g4). 'import' is included since it is script-level syntax.
    private static readonly string[] KeywordNames =
    [
        "let", "func", "if", "else", "while", "foreach", "in",
        "return", "break", "continue", "true", "false", "null"
    ];

    private FishboneCompletionCatalog(
        IReadOnlyList<FishboneCompletionData> globals,
        IReadOnlyDictionary<char, IReadOnlyList<FishboneCompletionData>> globalsByInitial,
        IReadOnlyList<FishboneCompletionData> keywords,
        IReadOnlyDictionary<string, IReadOnlyList<FishboneSignature>> signatures)
    {
        Globals = globals;
        GlobalsByInitial = globalsByInitial;
        Keywords = keywords;
        Signatures = signatures;
    }

    /// <summary>Built-ins, plugin functions, registered types and constants — the ambient API.</summary>
    public IReadOnlyList<FishboneCompletionData> Globals { get; }

    /// <summary>
    /// <see cref="Globals"/> bucketed by lowercase initial character. With plugins loaded this list
    /// runs into the thousands; the popup only ever needs the bucket matching the typed identifier's
    /// first letter, so handing the window just that bucket keeps per-keystroke filtering cheap.
    /// </summary>
    public IReadOnlyDictionary<char, IReadOnlyList<FishboneCompletionData>> GlobalsByInitial { get; }

    /// <summary>Language keywords.</summary>
    public IReadOnlyList<FishboneCompletionData> Keywords { get; }

    /// <summary>Callable name → its overload signatures, for parameter-hint (signature help) popups.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<FishboneSignature>> Signatures { get; }

    private static FishboneCompletionCatalog Build()
    {
        var keywords = KeywordNames
            .Select(k => new FishboneCompletionData(k, FishboneCompletionKind.Keyword, $"keyword  {k}"))
            .ToArray();

        var globals = new List<FishboneCompletionData>();
        var signatures = new Dictionary<string, IReadOnlyList<FishboneSignature>>(StringComparer.Ordinal);
        try
        {
            var config = new FishboneConfiguration();
            try
            {
                FishbonePluginLoader.LoadPlugins(FishbonePluginLoader.DefaultPluginsDirectory, config);
            }
            catch
            {
            }

            foreach (var (name, value) in config.BuiltIns)
            {
                (FishboneCompletionKind kind, IReadOnlyList<FishboneSignature> sigs) = Classify(name, value);

                string description = sigs.Count > 0
                    ? $"{KindLabel(kind)}  {sigs[0].ToCompactString()}"
                    : $"constant  {name} : {FriendlyType(value.GetType())}";
                globals.Add(new FishboneCompletionData(name, kind, description));

                if (sigs.Count > 0)
                    signatures[name] = sigs;
            }

            foreach (var name in config.Values.Keys)
                globals.Add(new FishboneCompletionData(name, FishboneCompletionKind.Variable, $"value  {name}"));
        }
        catch
        {
        }

        var globalsByInitial = globals
            .Where(g => g.Text.Length > 0)
            .GroupBy(g => char.ToLowerInvariant(g.Text[0]))
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FishboneCompletionData>)group.ToList());

        return new FishboneCompletionCatalog(globals, globalsByInitial, keywords, signatures);
    }

    private static string KindLabel(FishboneCompletionKind kind) => kind == FishboneCompletionKind.Type ? "type" : "function";

    private static (FishboneCompletionKind Kind, IReadOnlyList<FishboneSignature> Signatures) Classify(string name, object value)
    {
        switch (value)
        {
            case Delegate del:
                return (FishboneCompletionKind.Function, [SignatureFromMethod(name, del.Method)]);

            case INativeCallable native:
                return (FishboneCompletionKind.Function, [SignatureFromNative(name, native)]);

            case BoundMethod bound:
                var overloads = bound.Methods.Select(m => SignatureFromMethod(name, m)).ToList();
                return (FishboneCompletionKind.Function, overloads);

            case RegisteredType registered:
                var constructors = registered.Type.GetConstructors()
                    .OrderBy(c => c.GetParameters().Length)
                    .Select(c => SignatureFromConstructor(name, registered.Type, c))
                    .ToList();
                if (constructors.Count == 0)
                    constructors.Add(new FishboneSignature(name, [], registered.Type.Name));
                return (FishboneCompletionKind.Type, constructors);

            default:
                return (FishboneCompletionKind.Constant, []);
        }
    }

    private static FishboneSignature SignatureFromMethod(string name, MethodInfo method)
    {
        var parameters = method.GetParameters().Select(ToParameter).ToList();
        return new FishboneSignature(name, parameters, FriendlyType(method.ReturnType));
    }

    private static FishboneSignature SignatureFromConstructor(string name, Type type, ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters().Select(ToParameter).ToList();
        return new FishboneSignature(name, parameters, type.Name);
    }

    private static FishboneSignature SignatureFromNative(string name, INativeCallable native)
    {
        var parameters = native.Parameters
            .Select(p => new FishboneParameter(p.Name, FriendlyType(p.Type), MapDirection(p.Direction)))
            .ToList();
        return new FishboneSignature(name, parameters, ReturnType: null);
    }

    private static FishboneParameter ToParameter(ParameterInfo parameter)
    {
        var direction = parameter.IsOut
            ? FishboneParamDirection.Out
            : parameter.ParameterType.IsByRef ? FishboneParamDirection.Ref : FishboneParamDirection.In;
        return new FishboneParameter(parameter.Name ?? "arg", FriendlyType(parameter.ParameterType), direction);
    }

    private static FishboneParamDirection MapDirection(ParameterDirection direction) => direction switch
    {
        ParameterDirection.Out => FishboneParamDirection.Out,
        ParameterDirection.Ref => FishboneParamDirection.Ref,
        _ => FishboneParamDirection.In
    };

    private static string FriendlyType(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType() ?? type;
        if (type == typeof(void)) return "void";
        if (type == typeof(int)) return "int";
        if (type == typeof(double)) return "double";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(object)) return "object";
        return type.Name;
    }
}