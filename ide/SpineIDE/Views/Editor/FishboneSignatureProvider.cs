using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;

namespace SpineIDE.Views.Editor;

/// <summary>
/// Feeds AvaloniaEdit's <see cref="OverloadInsightWindow"/> the signature-help popup: it renders the
/// current overload's parameters with the active argument bolded and every parameter tagged
/// <c>in</c>/<c>out</c>/<c>ref</c>, and lets the user cycle overloads with up/down. Raises
/// <see cref="PropertyChanged"/> so the popup re-renders live as the caret moves between arguments.
/// </summary>
public sealed class FishboneSignatureProvider : IOverloadProvider, INotifyPropertyChanged
{
    private readonly IReadOnlyList<FishboneSignature> _signatures;
    private int _selectedIndex;
    private int _activeParameter;

    public FishboneSignatureProvider(IReadOnlyList<FishboneSignature> signatures) => _signatures = signatures;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Which argument the caret is currently on; drives the bolded parameter.</summary>
    public int ActiveParameterIndex
    {
        get => _activeParameter;
        set
        {
            if (_activeParameter == value)
                return;
            _activeParameter = value;
            Raise(nameof(CurrentHeader));
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            // OverloadInsightWindow drives this from up/down; wrap so cycling never goes out of range
            int count = _signatures.Count;
            int clamped = count == 0 ? 0 : ((value % count) + count) % count;
            if (_selectedIndex == clamped)
                return;
            _selectedIndex = clamped;
            Raise(nameof(SelectedIndex));
            Raise(nameof(CurrentIndexText));
            Raise(nameof(CurrentHeader));
            Raise(nameof(CurrentContent));
        }
    }

    public int Count => _signatures.Count;

    public string CurrentIndexText => _signatures.Count > 1 ? $"{_selectedIndex + 1} of {_signatures.Count}" : string.Empty;

    public object CurrentHeader => BuildHeader();

    public object CurrentContent => string.Empty;

    private object BuildHeader()
    {
        FishboneSignature signature = _signatures[_selectedIndex];
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var inlines = block.Inlines!;

        inlines.Add(new Run($"{signature.Name}("));
        for (int i = 0; i < signature.Parameters.Count; i++)
        {
            if (i > 0)
                inlines.Add(new Run(", "));

            FishboneParameter parameter = signature.Parameters[i];
            // "in" is the default and left implicit; only out/ref are annotated
            string prefix = parameter.Direction == FishboneParamDirection.In ? string.Empty : parameter.DirectionKeyword + " ";
            var run = new Run($"{prefix}{parameter.Type} {parameter.Name}");
            if (i == _activeParameter)
            {
                run.FontWeight = FontWeight.Bold;
                // tint out/ref so the direction of the active argument stands out
                if (parameter.Direction != FishboneParamDirection.In)
                    run.Foreground = Brushes.OrangeRed;
            }
            inlines.Add(run);
        }
        inlines.Add(new Run(")"));

        if (signature.ReturnType is not null)
            inlines.Add(new Run($" : {signature.ReturnType}"));

        return block;
    }

    private void Raise(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}