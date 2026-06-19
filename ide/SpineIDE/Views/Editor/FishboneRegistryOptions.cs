using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace SpineIDE.Views.Editor;

public class FishboneRegistryOptions : IRegistryOptions
{
    private readonly string _grammarResource = "SpineIDE.Assets.fishbone.tmLanguage.json";

    public IRawTheme GetDefaultTheme()
    {
        return ThemeReader.ReadThemeSync(new StreamReader(
             Assembly.GetExecutingAssembly().GetManifestResourceStream("SpineIDE.Assets.Themes.onedark-color-theme.json")!));
    }

    public IRawGrammar? GetGrammar(string scopeName)
    {
        if (scopeName == "source.fb")
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(_grammarResource);
            if (stream == null) throw new FileNotFoundException("Grammar resource not found.");
            return GrammarReader.ReadGrammarSync(new StreamReader(stream));
        }
        return null;
    }

    public ICollection<string>? GetInjections(string scopeName)
    {
        return null;
    }

    public IRawTheme GetTheme(string scopeName)
    {
        var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        return ThemeReader.ReadThemeSync(new StreamReader(
             Assembly.GetExecutingAssembly().GetManifestResourceStream("SpineIDE.Assets.Themes.onedark-color-theme.json")!));
    }
}