using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace SpineIDE.Views.Editor;

public sealed class FishboneRegistryOptions : IRegistryOptions
{
    private const string GrammarResource = "SpineIDE.Assets.fishbone.tmLanguage.json";
    private const string ThemeResource = "SpineIDE.Assets.Themes.onedark-color-theme.json";

    private static readonly Lazy<IRawGrammar> CachedGrammar = new(ReadGrammar);
    private static readonly Lazy<IRawTheme> CachedTheme = new(ReadTheme);

    public IRawTheme GetDefaultTheme()
    {
        return CachedTheme.Value;
    }

    public IRawGrammar? GetGrammar(string scopeName)
    {
        return scopeName == "source.fb" ? CachedGrammar.Value : null;
    }

    public ICollection<string>? GetInjections(string scopeName)
    {
        return null;
    }

    public IRawTheme GetTheme(string scopeName)
    {
        return CachedTheme.Value;
    }

    private static IRawGrammar ReadGrammar()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GrammarResource);
        if (stream == null)
            throw new FileNotFoundException("Grammar resource not found.", GrammarResource);

        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }

    private static IRawTheme ReadTheme()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ThemeResource);
        if (stream == null)
            throw new FileNotFoundException("Theme resource not found.", ThemeResource);

        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
    }
}