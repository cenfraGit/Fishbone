using System.Collections;
using Fishbone.Debugging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;

namespace Fishbone.DebugAdapter;

public sealed class DebugSnapshotHandles
{
    private readonly object _sync = new();
    private readonly Dictionary<long, DebugCallFrameSnapshot> _frames = [];
    private readonly Dictionary<long, object> _variables = [];
    private readonly Dictionary<object, long> _objectHandles = new(ReferenceEqualityComparer.Instance);
    private DebugPauseSnapshot? _snapshot;
    private long _nextHandle = 1;
    private long _nextFrame = 1000;

    public void SetSnapshot(DebugPauseSnapshot snapshot)
    {
        lock (_sync)
        {
            ClearLocked();
            _snapshot = snapshot;
            foreach (var frame in snapshot.CallStack)
                _frames[_nextFrame++] = frame;
        }
    }

    public void Clear()
    {
        lock (_sync) ClearLocked();
    }

    public IReadOnlyList<(long Id, DebugCallFrameSnapshot Frame)> GetFrames()
    {
        lock (_sync) return _frames.Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    public IReadOnlyList<Scope> GetScopes(long frameId)
    {
        lock (_sync)
        {
            if (_snapshot is null || !_frames.TryGetValue(frameId, out var frame))
                throw new InvalidOperationException("The stack frame is no longer available.");

            var scopes = new List<Scope>
            {
                CreateScope("Locals", frame.Variables, "locals")
            };

            if (_frames.First().Key == frameId)
                scopes.Add(CreateScope("Visible Variables", _snapshot.VisibleVariables, "locals"));

            var globals = _snapshot.CallStack[^1].Variables;
            if (_frames.Last().Key != frameId)
                scopes.Add(CreateScope("Globals", globals, null));

            return scopes;
        }
    }

    public IReadOnlyList<Variable> GetVariables(long reference, long? start = null, long? count = null)
    {
        lock (_sync)
        {
            if (_snapshot is null || !_variables.TryGetValue(reference, out var target))
                throw new InvalidOperationException("The variable reference is no longer available.");

            IEnumerable<DebugVariableSnapshot> children = target switch
            {
                IReadOnlyList<DebugVariableSnapshot> values => values,
                IList list => list.Cast<object?>().Select((value, index) => new DebugVariableSnapshot($"[{index}]", value)),
                IDictionary dictionary => EnumerateDictionary(dictionary),
                _ => []
            };

            if (start is > 0) children = children.Skip(checked((int)start.Value));
            if (count is > 0) children = children.Take(checked((int)count.Value));
            return children.Select(CreateVariable).ToArray();
        }
    }

    public DebugExceptionSnapshot? GetException()
    {
        lock (_sync) return _snapshot?.Exception;
    }

    private Scope CreateScope(string name, IReadOnlyList<DebugVariableSnapshot> values, string? hint)
    {
        long handle = AddHandle(values);
        return new Scope
        {
            Name = name,
            PresentationHint = hint,
            VariablesReference = handle,
            NamedVariables = values.Count,
            Expensive = false
        };
    }

    private Variable CreateVariable(DebugVariableSnapshot variable)
    {
        long reference = variable.Value is IList or IDictionary ? AddHandle(variable.Value) : 0;
        return new Variable
        {
            Name = variable.Name,
            Value = FormatValue(variable.Value),
            Type = variable.Value?.GetType().Name ?? "null",
            VariablesReference = reference,
            IndexedVariables = variable.Value is IList list ? list.Count : null,
            NamedVariables = variable.Value is IDictionary dictionary ? dictionary.Count : null
        };
    }

    private long AddHandle(object value)
    {
        if (_objectHandles.TryGetValue(value, out var existing)) return existing;
        long handle = _nextHandle++;
        _objectHandles[value] = handle;
        _variables[handle] = value;
        return handle;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        bool boolean => boolean ? "true" : "false",
        IList list => $"list ({list.Count})",
        IDictionary dictionary => $"dictionary ({dictionary.Count})",
        _ => value.ToString() ?? string.Empty
    };

    private static IEnumerable<DebugVariableSnapshot> EnumerateDictionary(IDictionary dictionary)
    {
        IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
            yield return new DebugVariableSnapshot($"[{FormatValue(enumerator.Key)}]", enumerator.Value);
    }

    private void ClearLocked()
    {
        _snapshot = null;
        _frames.Clear();
        _variables.Clear();
        _objectHandles.Clear();
        _nextHandle = 1;
        _nextFrame = 1000;
    }
}