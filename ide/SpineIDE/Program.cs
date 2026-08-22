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

        // Single instance: if a SpineIDE window is already open, hand it our startup
        // options (attach port / file to open) and exit — its window re-attaches in
        // place instead of a second window opening. If the primary can't be reached
        // (e.g. it is mid-shutdown), fall through and start normally.
        if (!SingleInstance.TryBecomePrimary() && SingleInstance.TrySignalPrimary(startupOptions))
            return;

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