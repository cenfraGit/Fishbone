using Avalonia.Controls;
using SpineIDE.Panels;

namespace SpineIDE.Views.Variables;

public partial class VariableDetailsWindow : Window
{
    public VariableDetailsWindow()
    {
        InitializeComponent();
    }

    public VariableDetailsWindow(string name, object? value)
        : this()
    {
        DataContext = new VariableDetailsVM(name, value);
    }
}