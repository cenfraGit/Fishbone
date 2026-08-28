using Dock.Model.Mvvm.Controls;

namespace SpineIDE.Panels;

public abstract class TextPanelVM : Tool
{
    private string _panelText = string.Empty;

    public string PanelText
    {
        get => _panelText;
        protected set => SetProperty(ref _panelText, value);
    }
}