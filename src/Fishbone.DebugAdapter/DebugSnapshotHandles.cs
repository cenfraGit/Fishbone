using Fishbone.Debugging;
using OmniSharp.Extensions.DebugAdapter.Protocol.Models;
using System.Collections;

namespace Fishbone.DebugAdapter;

public sealed class DebugSnapshotHandles
{
    /// <summary>Marks a variable whose value the debugger can show as an image.</summary>
    public const string ImageKind = "fishbone.image";

    private readonly FishboneConfiguration? _configuration;

    /// <summary>
    /// The configuration the script runs with, which says which values can be shown as images.
    /// </summary>
    public DebugSnapshotHandles(FishboneConfiguration? configuration = null)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// The image behind a variable reference. Only valid while paused, like any other reference.
    /// </summary>
    public FishboneImage GetImage(long reference)
    {
        lock (_sync)
        {
            if (_snapshot is null || !_variables.TryGetValue(reference, out var target))
                throw new InvalidOperationException("The variable reference is no longer available.");
            if (!IsImage(target))
                throw new InvalidOperationException("The variable isn't an image.");

            // the script is paused, so the visualizer can read the value safely
            FishboneImage? image;
            try
            {
                image = _configuration!.Visualize(target);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"The image couldn't be read: {exception.Message}", exception);
            }
            return image ?? throw new InvalidOperationException("The variable has no image to show.");
        }
    }

    private readonly object _sync = new();
    private readonly Dictionary<long, DebugCallFrameSnapshot> _frames = [];
    private readonly Dictionary<long, object> _variables = [];
    private readonly Dictionary<object, long> _objectHandles = new(ReferenceEqualityComparer.Instance);
    // the images an image value holds, by its handle, null when it's a single one. made for this
    // pause, so they're disposed when it ends
    private readonly Dictionary<long, IReadOnlyList<DebugVariableSnapshot>?> _imageChildren = [];
    private readonly List<IDisposable> _owned = [];
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

            if (_frames.First().Key == frameId
                && !SameVariableSet(frame.Variables, _snapshot.VisibleVariables))
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
                _ when _imageChildren.GetValueOrDefault(reference) is { } images => images,
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

    /// <summary>A frame of the current pause, or the top one when <paramref name="frameId"/> is null.</summary>
    public DebugCallFrameSnapshot GetFrame(long? frameId)
    {
        lock (_sync)
        {
            if (_snapshot is null || _frames.Count == 0)
                throw new InvalidOperationException("The script isn't paused.");
            if (frameId is null)
                return _frames.First().Value;
            return _frames.TryGetValue(frameId.Value, out var frame) ? frame
                : throw new InvalidOperationException("The stack frame is no longer available.");
        }
    }

    /// <summary>A value worked out while paused, like a watch's, shown like a variable of the pause.</summary>
    public Variable AddValue(string name, object? value)
    {
        lock (_sync)
        {
            if (_snapshot is null)
                throw new InvalidOperationException("The script continued.");
            return CreateVariable(new DebugVariableSnapshot(name, value));
        }
    }

    // Two scopes are treated as the same when they expose the same set of variable names — enough to
    // recognize that "Visible Variables" adds nothing over "Locals" at global scope.
    private static bool SameVariableSet(IReadOnlyList<DebugVariableSnapshot> a, IReadOnlyList<DebugVariableSnapshot> b)
    {
        if (a.Count != b.Count)
            return false;
        var names = new HashSet<string>(a.Count);
        foreach (var variable in a)
            names.Add(variable.Name);
        foreach (var variable in b)
            if (!names.Contains(variable.Name))
                return false;
        return true;
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
        bool isImage = IsImage(variable.Value);
        long reference = variable.Value is IList or IDictionary || isImage ? AddHandle(variable.Value!) : 0;
        IReadOnlyList<DebugVariableSnapshot>? images = isImage ? ImageChildren(reference, variable.Value!) : null;
        return new Variable
        {
            Name = variable.Name,
            Value = DebugValueFormatter.FormatValue(variable.Value),
            Type = DebugValueFormatter.FormatType(variable.Value),
            VariablesReference = reference,
            PresentationHint = isImage ? new VariablePresentationHint { Kind = ImageKind } : null,
            IndexedVariables = variable.Value is IList list ? list.Count : images?.Count,
            NamedVariables = variable.Value is IDictionary dictionary ? dictionary.Count : null
        };
    }

    // a visualizer's check runs plugin code, so a failing one just means "not an image"
    private bool IsImage(object? value)
    {
        try
        {
            return _configuration?.CanVisualize(value) == true;
        }
        catch
        {
            return false;
        }
    }

    // asked once per value and pause. a visualizer that fails just shows the value as one image
    private IReadOnlyList<DebugVariableSnapshot>? ImageChildren(long reference, object value)
    {
        if (_imageChildren.TryGetValue(reference, out var known))
            return known;
        IReadOnlyList<DebugVariableSnapshot>? images = null;
        try
        {
            if (_configuration!.ImageChildren(value) is { } children)
            {
                _owned.AddRange(children.Select(child => child.Value).OfType<IDisposable>());
                if (children.Count > 0)
                    images = children.Select(child => new DebugVariableSnapshot(child.Name, child.Value)).ToArray();
            }
        }
        catch
        {
        }
        _imageChildren[reference] = images;
        return images;
    }

    private long AddHandle(object value)
    {
        if (_objectHandles.TryGetValue(value, out var existing)) return existing;
        long handle = _nextHandle++;
        _objectHandles[value] = handle;
        _variables[handle] = value;
        return handle;
    }

    private static IEnumerable<DebugVariableSnapshot> EnumerateDictionary(IDictionary dictionary)
    {
        IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
        while (enumerator.MoveNext())
            yield return new DebugVariableSnapshot($"[{DebugValueFormatter.FormatDictionaryKey(enumerator.Key)}]", enumerator.Value);
    }

    private void ClearLocked()
    {
        _snapshot = null;
        _frames.Clear();
        _variables.Clear();
        _objectHandles.Clear();
        _imageChildren.Clear();
        foreach (IDisposable owned in _owned)
        {
            try
            {
                owned.Dispose();
            }
            catch
            {
                // plugin code. one that fails to let go doesn't stop the others
            }
        }
        _owned.Clear();
        _nextHandle = 1;
        _nextFrame = 1000;
    }
}