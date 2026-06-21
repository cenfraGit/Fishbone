using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SpineIDE.Panels;
using SpineIDE.Views.Input;
using SpineIDE.Views.Variables;
using SpineIDE.Views.Attach;

namespace SpineIDE.Services;

public interface IDialogService
{
    void Initialize(Window window);
    Task<IReadOnlyList<IStorageFile>?> OpenFileAsync();
    Task<IStorageFile?> SaveFileAsync(string suggestedName = "script.fb");
    Task ShowVariableDetailsAsync(string name, object? value);
    Task<string> ShowScriptInputAsync(CancellationToken cancellationToken);
    Task<RemoteAttachEndpoint?> ShowRemoteAttachAsync();
}

public class DialogService : IDialogService
{
    private Window? _mainWindow;

    public void Initialize(Window window) => _mainWindow = window;

    public async Task<IReadOnlyList<IStorageFile>?> OpenFileAsync()
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("DialogService: window was null");

        return await _mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
            FileTypeFilter = [SvsFileType]
        });
    }

    public async Task<IStorageFile?> SaveFileAsync(string suggestedName = "script.fb")
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("DialogService: window was null");

        return await _mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Script As",
            SuggestedFileName = suggestedName,
            DefaultExtension = ".fb",
            FileTypeChoices = [SvsFileType]
        });
    }

    public async Task ShowVariableDetailsAsync(string name, object? value)
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("DialogService: window was null");

        if (!VariableDisplayFormatter.IsCollection(value))
            return;

        var window = new VariableDetailsWindow(name, value);
        await window.ShowDialog(_mainWindow);
    }

    public async Task<string> ShowScriptInputAsync(CancellationToken cancellationToken)
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("DialogService: window was null");

        cancellationToken.ThrowIfCancellationRequested();

        var window = new ScriptInputWindow();
        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => window.Close((string?)null)));

        string? result = await window.ShowDialog<string?>(_mainWindow);
        cancellationToken.ThrowIfCancellationRequested();

        return result ?? throw new OperationCanceledException("Script input was cancelled.");
    }

    public async Task<RemoteAttachEndpoint?> ShowRemoteAttachAsync()
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("DialogService: window was null");
        return await new RemoteAttachWindow().ShowDialog<RemoteAttachEndpoint?>(_mainWindow);
    }

    private static FilePickerFileType SvsFileType => new("Fishbone Files")
    {
        Patterns = ["*.fb"],
        AppleUniformTypeIdentifiers = ["public.text"],
        MimeTypes = ["text/plain"]
    };
}