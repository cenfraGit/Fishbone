// --------------------------------------------------------------------------------
// FishboneEnvironment.cs
//
// a Fishbone environment consists of values + builtins that represent
// a scope in a script. most "environments" (again, representing
// scopes) have a parent environment, and the topmost environment
// represents a whole program's scope.
//
// the main script itself starts with its environment, then as the
// code enters new blocks that require scope (such as functions),
// we'll create a new environment (setting the current env as parent)
// and apply declaration/assignment rules accordingly.
//
// an environment consists of values and builtins
// - values: script defined elements at runtime
// - builtins: injected before fb execution
//
// note: builtins can be shadowed by declaring a new variable into the
// environment _values, but they're not actually "overwritten"
// completely (although they can be mutated via methods)
// --------------------------------------------------------------------------------

namespace Fishbone;

public class FishboneEnvironment
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private readonly FishboneEnvironment? _parent;
    private readonly Dictionary<string, object> _values = [];
    private readonly Dictionary<string, object> _builtins = [];

    public FishboneEnvironment? Parent => _parent;
    public IReadOnlyDictionary<string, object> Values { get => _values; }

    // --------------------------------------------------------------------------------
    // constructors
    // --------------------------------------------------------------------------------

    public FishboneEnvironment(FishboneEnvironment? parent = null)
    {
        _parent = parent;
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    // used to inject pre-existing objects so that the script can
    // access them.
    public void AddBuiltIn(string name, object value)
    {
        _builtins[name] = value;
    }

    // used to declare new variables. if a variable with the same name
    // is already declared in the current environment, will throw.
    public void Declare(string name, object value)
    {
        if (_values.ContainsKey(name))
            throw new FishboneRuntimeException($"Variable '{name}' is already declared.");

        _values[name] = value;
    }

    // used to assign a value to an already declared variable. if the
    // variable is not found on current environment (scope), will look
    // upwards via the parent chain until the variable is found, and
    // then assigned. if no variable with the name is found, will
    // throw.
    public void Assign(string name, object value)
    {
        if (_values.ContainsKey(name))
        {
            _values[name] = value;
            return;
        }

        if (_parent != null)
        {
            _parent.Assign(name, value);
            return;
        }

        throw new FishboneRuntimeException($"Undefined variable '{name}'.");
    }

    // helper to outside: check if a variable with a specific name is
    // defined in the local-parent chain _values. note: we're not
    // checking _builtins because this helper method is used
    // explicitly to know if we should declare/assign a value via an
    // out parameter, and we're not supposed to overwrite builtins
    // (but shadowing is fine).
    public bool IsDefined(string name) =>
        _values.ContainsKey(name) || (_parent?.IsDefined(name) ?? false);

    public bool TryGetValue(string name, out object? value)
    {
        if (_values.TryGetValue(name, out value))
            return true;
        if (_builtins.TryGetValue(name, out value))
            return true;
        if (_parent != null)
            return _parent.TryGetValue(name, out value);
        value = null;
        return false;
    }

    public object GetValue(string name)
    {
        if (_values.TryGetValue(name, out var val))
            return val;
        if (_builtins.TryGetValue(name, out var builtin))
            return builtin;
        if (_parent != null)
            return _parent.GetValue(name);
        throw new FishboneRuntimeException($"Undefined variable '{name}'.");
    }
}