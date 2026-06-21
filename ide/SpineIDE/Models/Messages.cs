using System.Collections.Generic;
using Fishbone.Core;
using Fishbone.DebugClient;

namespace SpineIDE.Models.Messages;

// used to "request" the active script's code
public enum ScriptLaunchMode { Run, Debug }
public record MessageRunActiveScript(ScriptLaunchMode Mode);

// used to broadcast the script code
public record MessageExecute(Script Script, ScriptLaunchMode Mode, IReadOnlyList<int> BreakpointLines);

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

public record MessageVariableDetailsRequested(string Name, object? Value);
public record MessageDebugPaused(FishbonePauseSnapshot Snapshot, IFishboneDebugClientSession Session);
public record MessageDebugContinued;
public record MessageDebugLocationChanged(string SourceId, int? Line);
public record MessageDebugEditingChanged(string SourceId, bool IsDebugging);

public enum EditorAction { Cut, Copy, Paste, Undo, Redo, AddLineComment, RemoveLineComment }
public record MessageEditorAction(EditorAction Action);
public record MessageInsertSnippet(string Text);