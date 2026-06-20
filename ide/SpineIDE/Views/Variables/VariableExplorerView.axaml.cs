using Avalonia.Controls;
using Avalonia.Input;
using SpineIDE.Panels;

namespace SpineIDE.Views.Variables;

public partial class VariableExplorerView : UserControl
{
    public VariableExplorerView()
    {
        InitializeComponent();
        VariablesGrid.DoubleTapped += OnVariablesGridDoubleTapped;
    }

    private void OnVariablesGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not VariableExplorerVM viewModel)
            return;

        var selectedVariable = VariablesGrid.SelectedItem as VariableItem ?? viewModel.SelectedVariable;
        viewModel.OpenVariableDetailsCommand.Execute(selectedVariable);
    }
}