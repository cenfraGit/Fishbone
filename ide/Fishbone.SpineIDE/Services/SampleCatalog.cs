using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SpineIDE.Services;

public sealed record SampleDefinition(string FileName, string DisplayName);

/// <summary>
/// The samples shown in the Help menu. The catalog is discovered from the embedded
/// resources (the csproj embeds every <c>samples/*.fb</c>), so adding a sample is just
/// dropping a file into the samples directory and rebuilding. The display name comes
/// from an optional <c>// title: ...</c> comment on the sample's first line, or is
/// derived from the file name (<c>bubble_sort.fb</c> to "Bubble Sort").
/// </summary>
public static class SampleCatalog
{
    private const string ResourcePrefix = "SpineIDE.Samples.";
    private const string TitleMarker = "// title:";

    public static IReadOnlyList<SampleDefinition> Samples { get; } = DiscoverSamples();

    public static string Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string resourceName = ResourcePrefix + fileName;
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException($"Sample '{fileName}' was not found.", fileName);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<SampleDefinition> DiscoverSamples() =>
        Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        && name.EndsWith(".fb", StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..])
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .Select(fileName => new SampleDefinition(fileName, ResolveDisplayName(fileName)))
            .ToArray();

    private static string ResolveDisplayName(string fileName)
    {
        string firstLine = ReadFirstLine(fileName);
        if (firstLine.StartsWith(TitleMarker, StringComparison.OrdinalIgnoreCase))
        {
            string title = firstLine[TitleMarker.Length..].Trim();
            if (title.Length > 0)
                return title;
        }

        // bubble_sort.fb to "Bubble Sort"
        string stem = Path.GetFileNameWithoutExtension(fileName);
        var words = stem
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word));
        return string.Join(' ', words);
    }

    private static string ReadFirstLine(string fileName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
            return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadLine()?.Trim() ?? string.Empty;
    }
}