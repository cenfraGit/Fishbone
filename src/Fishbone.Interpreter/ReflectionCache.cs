// --------------------------------------------------------------------------------
// ReflectionCache.cs
//
// every time a script calls a method or something reflection-based,
// the interpreter needs reflection data from that object (like
// parameter lists, which method matches, etc). this operation can be
// slow if done every time its needed.
//
// the ReflectionCache class provides a way to cache the answer to the
// first lookup, attempting to make further calls faster. mainly just
// for speed optimization, but no actual behavior changes.
// --------------------------------------------------------------------------------

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
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static;

    private static readonly ConcurrentDictionary<MethodBase, ParameterInfo[]> Parameters = new();
    private static readonly ConcurrentDictionary<MethodInfo, MethodInvoker> Invokers = new();
    private static readonly ConcurrentDictionary<ConstructorInfo, ConstructorInvoker> ConstructorInvokers = new();
    private static readonly ConcurrentDictionary<Type, ConstructorInfo[]> Constructors = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SingleParameterIndexers = new();
    private static readonly ConcurrentDictionary<(Type Type, string Name, bool IsStatic), MemberLookup> Members = new();

    public static ParameterInfo[] GetParameters(MethodBase method) =>
        Parameters.GetOrAdd(method, static m => m.GetParameters());

    public static MethodInvoker GetInvoker(MethodInfo method) =>
        Invokers.GetOrAdd(method, static m => MethodInvoker.Create(m));

    public static ConstructorInvoker GetConstructorInvoker(ConstructorInfo constructor) =>
        ConstructorInvokers.GetOrAdd(constructor, static c => ConstructorInvoker.Create(c));

    public static ConstructorInfo[] GetConstructors(Type type) =>
        Constructors.GetOrAdd(type, static t => t.GetConstructors(InstanceMembers));

    public static PropertyInfo[] GetSingleParameterIndexers(Type type) =>
        SingleParameterIndexers.GetOrAdd(type, static t => t
            .GetProperties(InstanceMembers)
            .Where(property => property.GetIndexParameters().Length == 1)
            .ToArray());

    /// <summary>
    /// Resolves a member name against a type, preserving the interpreter's existing resolution
    /// order: a non-indexed property, then a field, then a method group. isStatic picks static
    /// or instance members.
    /// </summary>
    public static MemberLookup ResolveMember(Type type, string name, bool isStatic) => 
        Members.GetOrAdd((type, name, isStatic), static key => ComputeMember(key.Type, key.Name, key.IsStatic));

    /// <summary>
    /// Every member name a script can reach on a type, each resolved with <see cref="ResolveMember"/>
    /// so the list matches what member access finds.
    /// </summary>
    public static IEnumerable<(string Name, MemberLookup Lookup)> ListMembers(Type type, bool isStatic)
    {
        BindingFlags bindingScope = (isStatic) ? StaticMembers : InstanceMembers;
        var names = type.GetProperties(bindingScope)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Select(property => property.Name)
            .Concat(type.GetFields(bindingScope).Select(field => field.Name))
            .Concat(type.GetMethods(bindingScope).Where(method => !method.IsSpecialName).Select(method => method.Name))
            .Distinct();
        return names.Select(name => (name, ResolveMember(type, name, isStatic)));
    }

    private static MemberLookup ComputeMember(Type type, string name, bool isStatic)
    {
        BindingFlags bindingScope = (isStatic) ? StaticMembers : InstanceMembers;

        var property = type
            .GetProperties(bindingScope)
            .FirstOrDefault(prop => prop.Name == name && prop.GetIndexParameters().Length == 0);
        if (property is not null)
            return new MemberLookup { Property = property };

        var field = type
            .GetFields(bindingScope)
            .FirstOrDefault(fieldInfo => fieldInfo.Name == name);
        if (field is not null)
            return new MemberLookup { Field = field };

        var methods = type
            .GetMethods(bindingScope)
            .Where(method => method.Name == name && !method.IsSpecialName)
            // a 'new'-style redeclaration (e.g. Exception.GetType hiding Object.GetType) surfaces
            // as two identical signatures; keep only the most-derived one so overload resolution
            // doesn't consider the call ambiguous
            .GroupBy(SignatureKey)
            .Select(group => group.OrderByDescending(m => DerivationDepth(m.DeclaringType)).First())
            .ToArray();
        if (methods.Length > 0)
            return new MemberLookup { Methods = methods };

        return MemberLookup.None;
    }

    private static string SignatureKey(MethodInfo method) =>
        $"{method.GetGenericArguments().Length}:{string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name))}";

    private static int DerivationDepth(Type? type)
    {
        int depth = 0;
        while (type is not null)
        {
            depth++;
            type = type.BaseType;
        }
        return depth;
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