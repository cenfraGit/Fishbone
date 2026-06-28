using System.Collections.Concurrent;
using System.Reflection;

namespace Fishbone.Interpreter;

/// <summary>
/// Memoizes the reflection metadata that the interpreter touches on every member access and
/// method call. The CLR reflection APIs (GetProperties/GetFields/GetMethods/GetParameters) and
/// MethodInvoker code generation are expensive to repeat per evaluation, so we cache them keyed
/// on the relevant <see cref="Type"/> / <see cref="MethodInfo"/>. None of this changes behavior;
/// it only removes repeated work from the hot dispatch path.
/// </summary>
internal static class ReflectionCache
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.Instance;

    private static readonly ConcurrentDictionary<MethodInfo, ParameterInfo[]> Parameters = new();
    private static readonly ConcurrentDictionary<MethodInfo, MethodInvoker> Invokers = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SingleParameterIndexers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name), MemberLookup> Members = new();

    public static ParameterInfo[] GetParameters(MethodInfo method) =>
        Parameters.GetOrAdd(method, static m => m.GetParameters());

    public static MethodInvoker GetInvoker(MethodInfo method) =>
        Invokers.GetOrAdd(method, static m => MethodInvoker.Create(m));

    public static PropertyInfo[] GetSingleParameterIndexers(Type type) =>
        SingleParameterIndexers.GetOrAdd(type, static t => t
            .GetProperties(InstanceMembers)
            .Where(property => property.GetIndexParameters().Length == 1)
            .ToArray());

    /// <summary>
    /// Resolves a member name against a type, preserving the interpreter's existing resolution
    /// order: a non-indexed property, then a field, then a method group.
    /// </summary>
    public static MemberLookup ResolveMember(Type type, string name) =>
        Members.GetOrAdd((type, name), static key => ComputeMember(key.Type, key.Name));

    private static MemberLookup ComputeMember(Type type, string name)
    {
        var property = type
            .GetProperties(InstanceMembers)
            .FirstOrDefault(prop => prop.Name == name && prop.GetIndexParameters().Length == 0);
        if (property is not null)
            return new MemberLookup { Property = property };

        var field = type
            .GetFields(InstanceMembers)
            .FirstOrDefault(fieldInfo => fieldInfo.Name == name);
        if (field is not null)
            return new MemberLookup { Field = field };

        var methods = type
            .GetMethods(InstanceMembers)
            .Where(method => method.Name == name && !method.IsSpecialName)
            .ToArray();
        if (methods.Length > 0)
            return new MemberLookup { Methods = methods };

        return MemberLookup.None;
    }
}

/// <summary>
/// The resolved result of a member-name lookup. At most one of the members is populated;
/// <see cref="None"/> represents "no public member with that name".
/// </summary>
internal sealed class MemberLookup
{
    public static readonly MemberLookup None = new();

    public PropertyInfo? Property { get; init; }
    public FieldInfo? Field { get; init; }
    public MethodInfo[]? Methods { get; init; }
}