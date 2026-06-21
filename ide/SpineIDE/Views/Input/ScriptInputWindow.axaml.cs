using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SpineIDE.Views.Input;

public partial class ScriptInputWindow : Window
{
    public ScriptInputWindow()
    {
        InitializeComponent();
        Opened += (_, _) => InputTextBox.Focus();
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Submit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }

    private void OnSubmitClicked(object? sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Submit()
    {
        Close(InputTextBox.Text ?? string.Empty);
    }
}