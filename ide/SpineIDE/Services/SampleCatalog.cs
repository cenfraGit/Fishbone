using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SpineIDE.Services;

public sealed record SampleDefinition(string FileName, string DisplayName);

public static class SampleCatalog
{
    public static IReadOnlyList<SampleDefinition> Samples { get; } =
    [
        new("area_circle.fb", "Area of a Circle"),
        new("bubble_sort.fb", "Bubble Sort")
    ];

    public static string Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string resourceName = $"SpineIDE.Samples.{fileName}";
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new FileNotFoundException($"Sample \"{fileName}\" was not found.", fileName);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}