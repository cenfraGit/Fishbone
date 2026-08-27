using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Fishbone.DebugClient;
using Microsoft.Extensions.DependencyInjection;
using SpineIDE.Panels;
using SpineIDE.Services;
using SpineIDE.Views.Main;
using System;

namespace SpineIDE;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    internal static SpineIdeStartupOptions StartupOptions { get; set; } = new();

    private IDisposable? _singleInstanceServer;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        IServiceCollection collection = new ServiceCollection();

        collection.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        collection.AddSingleton<IDialogService, DialogService>();
        collection.AddSingleton<OutputPanelVM>();
        collection.AddSingleton<IErrorService, ErrorService>();
        collection.AddSingleton<ErrorPanelVM>();
        collection.AddSingleton<IFishboneDapHostLocator, FishboneDapHostLocator>();
        collection.AddSingleton<IFishboneDebugClientSessionFactory, FishboneDebugClientSessionFactory>();
        collection.AddSingleton(StartupOptions);

        collection.AddTransient<MainWindowVM>();

        ServiceProvider = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            var dialogService = ServiceProvider.GetRequiredService<IDialogService>();
            dialogService.Initialize(mainWindow);
            var mainWindowViewModel = ServiceProvider.GetRequiredService<MainWindowVM>();
            mainWindow.DataContext = mainWindowViewModel;
            desktop.MainWindow = mainWindow;
            if (StartupOptions.AttachPort is int attachPort)
                Dispatcher.UIThread.Post(() => _ = mainWindowViewModel.AttachRemoteAsync("127.0.0.1", attachPort));
            if (StartupOptions.FilePath is string filePath)
                Dispatcher.UIThread.Post(() => _ = mainWindowViewModel.OpenFileFromPathAsync(filePath));

            // Single instance: as the primary, accept startup options forwarded by later
            // SpineIDE launches (e.g. FlexInspect's debug button) and service them in this
            // window instead of letting a second one open.
            if (SingleInstance.IsPrimary)
            {
                _singleInstanceServer = SingleInstance.StartServer(options =>
                    Dispatcher.UIThread.Post(() => HandleForwardedStartup(mainWindow, mainWindowViewModel, options)));
                desktop.Exit += (_, _) => _singleInstanceServer?.Dispose();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Services a startup forwarded from a secondary launch: brings the window to the
    /// front, then re-attaches to the new debug endpoint and/or opens the requested file.
    /// AttachRemoteAsync already cancels any session in progress, so a debug press while
    /// an old session is still open simply replaces it.
    /// </summary>
    private static void HandleForwardedStartup(MainWindow mainWindow, MainWindowVM viewModel, SpineIdeStartupOptions options)
    {
        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
        mainWindow.Activate();

        if (options.AttachPort is int port)
            _ = viewModel.AttachRemoteAsync("127.0.0.1", port);
        if (options.FilePath is string filePath)
            _ = viewModel.OpenFileFromPathAsync(filePath);
    }
}