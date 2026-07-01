using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SpineIDE.Models;

namespace SpineIDE.Views.Input;

public partial class SaveConfirmationWindow : Window
{
    public SaveConfirmationWindow() : this("this file")
    {
    }

    public SaveConfirmationWindow(string fileName)
    {
        InitializeComponent();
        MessageTextBlock.Text = $"Do you want to save changes to \"{fileName}\"?";
        KeyDown += OnWindowKeyDown;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(SaveConfirmationResult.Cancel);
        }
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e) => Close(SaveConfirmationResult.Save);

    private void OnDiscardClicked(object? sender, RoutedEventArgs e) => Close(SaveConfirmationResult.Discard);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(SaveConfirmationResult.Cancel);
}