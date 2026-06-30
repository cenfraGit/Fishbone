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
    private const string DarkThemeResource = "SpineIDE.Assets.Themes.onedark-color-theme.json";
    private const string LightThemeResource = "SpineIDE.Assets.Themes.onelight-color-theme.json";

    private static readonly Lazy<IRawGrammar> CachedGrammar = new(ReadGrammar);
    private static readonly Lazy<IRawTheme> CachedDarkTheme = new(() => ReadTheme(DarkThemeResource));
    private static readonly Lazy<IRawTheme> CachedLightTheme = new(() => ReadTheme(LightThemeResource));

    /// <summary>The theme used when <see cref="GetDefaultTheme"/> is consulted; defaults to dark.</summary>
    public bool IsLight { get; set; }

    /// <summary>Returns the editor theme for the given variant, so the view can swap on theme change.</summary>
    public IRawTheme GetTheme(bool light) => light ? CachedLightTheme.Value : CachedDarkTheme.Value;

    public IRawTheme GetDefaultTheme()
    {
        return GetTheme(IsLight);
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
        return GetTheme(IsLight);
    }

    private static IRawGrammar ReadGrammar()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GrammarResource);
        if (stream == null)
            throw new FileNotFoundException("Grammar resource not found.", GrammarResource);

        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }

    private static IRawTheme ReadTheme(string themeResource)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(themeResource);
        if (stream == null)
            throw new FileNotFoundException("Theme resource not found.", themeResource);

        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
    }
}