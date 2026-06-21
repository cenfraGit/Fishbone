using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using SpineIDE.Views.Main;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using SpineIDE.Services;
using System;
using SpineIDE.Panels;
using Fishbone.DebugClient;

namespace SpineIDE;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }

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

        collection.AddTransient<MainWindowVM>();

        ServiceProvider = collection.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            var dialogService = ServiceProvider.GetRequiredService<IDialogService>();
            dialogService.Initialize(mainWindow);
            mainWindow.DataContext = ServiceProvider.GetRequiredService<MainWindowVM>();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}