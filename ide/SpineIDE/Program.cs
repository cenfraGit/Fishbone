using Avalonia;
using System;

namespace SpineIDE;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SpineIdeStartupOptions.TryParse(args, Console.Error, out var startupOptions))
        {
            Environment.ExitCode = 2;
            return;
        }

        App.StartupOptions = startupOptions;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}