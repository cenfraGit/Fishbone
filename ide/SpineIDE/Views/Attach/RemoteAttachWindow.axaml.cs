using Avalonia.Controls;
using Avalonia.Interactivity;
using SpineIDE.Services;

namespace SpineIDE.Views.Attach;

public partial class RemoteAttachWindow : Window
{
    public RemoteAttachWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnAttach(object? sender, RoutedEventArgs e)
    {
        string host = HostBox.Text?.Trim() ?? string.Empty;
        int port = checked((int)(PortBox.Value ?? 0));
        if (host.Length == 0 || port is < 1 or > 65535)
            return;
        Close(new RemoteAttachEndpoint(host, port));
    }
}