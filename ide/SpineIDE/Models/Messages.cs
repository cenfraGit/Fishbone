using Fishbone.Core;
using SpineIDE.Models;

namespace SpineIDE.Models.Messages;

// used to "request" the active script's code
public class MessageRunActiveScript { }

// used to broadcast the script code
public record MessageExecute(Script Script);

public class MessageExecutionFinished
{
    public string ScriptName { get; }
    public FishboneEnvironment Environment { get; }

    public MessageExecutionFinished(string scriptName, FishboneEnvironment environment)
    {
        this.ScriptName = scriptName;
        this.Environment = environment;
    }
}

public class MessageVariableSelected
{
    public string Name { get; }
    public object? Value { get; }

    public MessageVariableSelected(string name, object? value)
    {
        this.Name = name;
        this.Value = value;
    }
}

public record MessageVariableDetailsRequested(string Name, object? Value);

public enum EditorAction { Cut, Copy, Paste, Undo, Redo, AddLineComment, RemoveLineComment }
public record MessageEditorAction(EditorAction Action);
public record MessageInsertSnippet(string Text);