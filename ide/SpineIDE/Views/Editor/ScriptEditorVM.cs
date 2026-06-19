using System.IO;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using SpineIDE.Models;
using SpineIDE.Models.Messages;

namespace SpineIDE.Views.Editor;

public partial class ScriptEditorVM : Document, IRecipient<MessageRunActiveScript>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private string _scriptNameTemp = string.Empty; // used if no path specified

    [ObservableProperty] TextDocument _scriptDocument;

    public string? ScriptPath { get; set; }
    public string ScriptName
    {
        get
        {
            if (this.ScriptPath is null) return _scriptNameTemp;
            return Path.GetFileName(this.ScriptPath);
        }
    }

    // --------------------------------------------------------------------------------
    // constructor
    // --------------------------------------------------------------------------------

    public ScriptEditorVM(string name, string? path, string contents)
    {
        this.Title = name;
        this._scriptNameTemp = name;
        this.ScriptPath = path;
        this.ScriptDocument = new(contents);

        WeakReferenceMessenger.Default.Register(this);
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public async void Receive(MessageRunActiveScript m)
    {
        // whenever we receive a "run script" message, we'll broadcast back a message with the script data
        if (this.IsActive)
        {
            var scriptData = new Script(this.ScriptName, this.ScriptPath, ScriptDocument.Text);
            WeakReferenceMessenger.Default.Send(new MessageExecute(scriptData));
        }
    }
}