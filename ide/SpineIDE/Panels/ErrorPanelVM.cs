using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using SpineIDE.Services;

namespace SpineIDE.Panels;

public class ErrorPanelVM : TextPanelVM
{
    public ObservableCollection<ScriptExecutionError> Errors { get; }

    public ErrorPanelVM(IErrorService errorService)
    {
        Errors = errorService.Errors;
        Errors.CollectionChanged += OnErrorsChanged;
        RefreshErrorText();
    }

    private void OnErrorsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshErrorText();
    }

    private void RefreshErrorText()
    {
        PanelText = string.Join(
            Environment.NewLine,
            Errors.Select(error => error.HasLocation
                ? $"{error.LocationDisplay}: {error.ExMessage}"
                : error.ExMessage));
    }
}