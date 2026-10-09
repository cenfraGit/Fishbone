// --------------------------------------------------------------------------------
// FishboneAnalysis.cs
//
// a static analyzer. it walks a script's AST without running it and works out the
// types it can be sure of, for completion and diagnostics. when it isn't sure, the
// type is unknown, and an unknown type never produces an error.
// --------------------------------------------------------------------------------

using Fishbone.Ast;
using Fishbone.Interpreter;
using Fishbone.Parser;

namespace Fishbone;

/// <summary>A problem found without running the script. Columns start at 1, and the end is just past the last character.</summary>
public sealed record FishboneDiagnostic(int Line, int Column, int EndLine, int EndColumn, string Message);

/// <summary>A script variable, with its type when the analyzer is sure of it.</summary>
/// <param name="Parameters">The parameter names when it's a script function, otherwise null.</param>
public sealed record FishboneScriptVariable(string Name, Type? Type, IReadOnlyList<string>? Parameters = null);

/// <summary>
/// The type of an expression. <see cref="IsStatic"/> means the expression names the type itself,
/// like a registered type, so <c>.</c> reaches its static members.
/// </summary>
public sealed record FishboneExpressionType(Type Type, bool IsStatic);

/// <summary>
/// What the analyzer knows about a script. Made by <see cref="Analyze"/>.
/// </summary>
/// <remarks>
/// A variable has a known type when it's declared once from an expression of known type and never
/// assigned again. Script functions, their parameters and anything that depends on control flow
/// are unknown. The rules for numbers, members and calls match the interpreter's.
/// </remarks>
public sealed partial class FishboneAnalysis
{
    // Exact means the value's runtime type is Type itself, not a type derived from it
    private readonly record struct Fact(Type Type, bool IsStatic, bool Exact);

    private sealed class Scope(Scope? parent, bool isFunction, long start, long end)
    {
        public Scope? Parent { get; } = parent;
        public bool IsFunction { get; } = isFunction;
        public long Start { get; } = start;
        public long End { get; } = end;
        public List<Scope> Children { get; } = [];
        public List<Declaration> Declarations { get; } = [];
    }

    // the type is worked out the first time it's needed
    private sealed class Declaration(string name, long visibleFrom, Func<Fact?> type, IReadOnlyList<string>? parameters = null)
    {
        public IReadOnlyList<string>? Parameters { get; } = parameters;
        private readonly Lazy<Fact?> _type = new(type, LazyThreadSafetyMode.None);
        public string Name { get; } = name;
        public long VisibleFrom { get; } = visibleFrom;
        public Fact? Type => _type.Value;
    }

    private readonly FishboneDescription _description;
    private readonly Dictionary<string, FishboneSymbol> _symbols;
    // names a script assigns, or passes as out or ref. their type can change, so it's unknown
    private readonly HashSet<string> _assigned = new(StringComparer.Ordinal);
    private readonly Scope _root = new(null, false, 0, long.MaxValue);
    private readonly List<FishboneDiagnostic> _diagnostics = [];

    private FishboneAnalysis(string code, FishboneDescription description)
    {
        _description = description;
        _symbols = description.Symbols.ToDictionary(symbol => symbol.Name, StringComparer.Ordinal);

        AstNode program;
        try
        {
            program = ASTParser.Parse(code);
        }
        catch (FishboneParseException exception)
        {
            foreach (var error in exception.Errors)
                _diagnostics.Add(new(error.Line, error.Column, error.Line,
                    error.Column + Math.Max(1, error.OffendingText?.Length ?? 0), error.Message));
            return;
        }

        Parsed = true;
        CollectAssigned(program);
        Build(program, _root);
        Check(program);
    }

    /// <summary>Analyzes a script against what a configuration puts into it.</summary>
    public static FishboneAnalysis Analyze(string code, FishboneDescription description) => new(code, description);

    /// <summary>False when the script has syntax errors. They're in <see cref="Diagnostics"/>, and nothing else is known.</summary>
    public bool Parsed { get; }

    /// <summary>Syntax errors, or else the problems the analyzer is sure of.</summary>
    public IReadOnlyList<FishboneDiagnostic> Diagnostics => _diagnostics;

    /// <summary>The script variables, functions and parameters visible at a position.</summary>
    public IReadOnlyList<FishboneScriptVariable> VisibleAt(int line, int column)
    {
        long position = Position(line, column);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var variables = new List<FishboneScriptVariable>();
        for (var scope = Innermost(position); scope is not null; scope = scope.Parent)
        {
            // the latest declaration of a name hides the earlier ones
            foreach (var declaration in Enumerable.Reverse(scope.Declarations))
                if (declaration.VisibleFrom <= position && seen.Add(declaration.Name))
                    variables.Add(new(declaration.Name, _assigned.Contains(declaration.Name) ? null : declaration.Type?.Type,
                        _assigned.Contains(declaration.Name) ? null : declaration.Parameters));
        }
        return variables;
    }

    /// <summary>
    /// The type of an expression written at a position, like the text before a <c>.</c> the user
    /// just typed. Null when it doesn't parse or the type isn't known.
    /// </summary>
    public FishboneExpressionType? TypeOf(string expression, int line, int column)
    {
        AstNode node;
        try
        {
            node = ASTParser.ParseExpression(expression);
        }
        catch (FishboneParseException)
        {
            return null;
        }
        return TypeOf(node, Position(line, column)) is { } fact ? new(fact.Type, fact.IsStatic) : null;
    }

    // --------------------------------------------------------------------------------
    // scopes
    // --------------------------------------------------------------------------------

    // line and column packed into one number, so positions compare with < and >
    private static long Position(int line, int column) => ((long)line << 32) + column;

    private static long Start(AstNode node) => Position(node.Line, node.Column);

    private static long End(AstNode node) => node.EndLine == 0 ? long.MaxValue : Position(node.EndLine, node.EndColumn);

    private void CollectAssigned(AstNode node)
    {
        if (node is AssignmentNode assignment)
            _assigned.Add(assignment.Name);
        if (node is CallNode call)
            foreach (var argument in call.Arguments)
                if (argument is { Modifier: not ArgumentModifier.None, Value: IdentifierNode name })
                    _assigned.Add(name.Name);
        foreach (var child in Children(node))
            CollectAssigned(child);
    }

    // the scopes follow the environments the interpreter makes: one per block, loop, catch and call
    private void Build(AstNode node, Scope scope)
    {
        switch (node)
        {
            case DeclarationNode declaration:
                Build(declaration.Value, scope);
                // the value is evaluated before the name exists, so the name is visible after the statement
                long at = Start(declaration.Value);
                scope.Declarations.Add(new(declaration.Name, End(declaration), () => TypeOf(declaration.Value, at)));
                break;

            case FunctionDefinitionNode function:
                scope.Declarations.Add(new(function.Name, End(function), () => null, function.Parameters));
                var body = Open(scope, function.Body, isFunction: true);
                foreach (var parameter in function.Parameters)
                    body.Declarations.Add(new(parameter, body.Start, () => null));
                foreach (var statement in function.Body.Statements)
                    Build(statement, body);
                break;

            case BlockNode block:
                var inner = Open(scope, block, isFunction: false);
                foreach (var statement in block.Statements)
                    Build(statement, inner);
                break;

            case ForeachNode loop:
                Build(loop.Iterable, scope);
                var foreachScope = Open(scope, loop, isFunction: false);
                foreachScope.Declarations.Add(new(loop.IteratorName, foreachScope.Start, () => null));
                Build(loop.Body, foreachScope);
                break;

            case ForNode loop:
                foreach (var bound in new[] { loop.Start, loop.End, loop.Step }.OfType<AstNode>())
                    Build(bound, scope);
                var forScope = Open(scope, loop, isFunction: false);
                forScope.Declarations.Add(new(loop.IteratorName, forScope.Start, () => LoopVariable(loop, Start(loop))));
                Build(loop.Body, forScope);
                break;

            case TryNode tryNode:
                Build(tryNode.TryBlock, scope);
                if (tryNode.CatchBlock is not null)
                {
                    var catchScope = Open(scope, tryNode.CatchBlock, isFunction: false);
                    // the script gets the actual exception, which can be any type derived from Exception
                    if (tryNode.ExceptionName is not null)
                        catchScope.Declarations.Add(new(tryNode.ExceptionName, catchScope.Start, () => new Fact(typeof(Exception), false, false)));
                    foreach (var statement in tryNode.CatchBlock.Statements)
                        Build(statement, catchScope);
                }
                if (tryNode.FinallyBlock is not null)
                    Build(tryNode.FinallyBlock, scope);
                break;

            default:
                foreach (var child in Children(node))
                    Build(child, scope);
                break;
        }
    }

    private static Scope Open(Scope parent, AstNode node, bool isFunction)
    {
        var scope = new Scope(parent, isFunction, Start(node), Math.Min(End(node), parent.End));
        parent.Children.Add(scope);
        return scope;
    }

    private Scope Innermost(long position)
    {
        var scope = _root;
        while (scope.Children.FirstOrDefault(child => child.Start <= position && position < child.End) is { } child)
            scope = child;
        return scope;
    }

    // what a name refers to at a position, following the interpreter's lookup: a type keyword
    // first, then the nearest script declaration, then the configuration
    private Fact? Lookup(string name, long position)
    {
        if (ReservedTypes.PrimitiveTypeNames.TryGetValue(name, out var keywordType))
            return new Fact(keywordType, true, true);
        if (_assigned.Contains(name))
            return null;

        bool leftFunction = false;
        for (var scope = Innermost(position); scope is not null; scope = scope.Parent)
        {
            if (scope.Declarations.LastOrDefault(declaration => declaration.Name == name && declaration.VisibleFrom <= position) is { } visible)
                return visible.Type;
            // a function runs when it's called, so it can also see what its outer scopes declare later
            if (leftFunction && scope.Declarations.Any(declaration => declaration.Name == name))
                return null;
            leftFunction |= scope.IsFunction;
        }

        return _symbols.TryGetValue(name, out var symbol) ? symbol.Kind switch
        {
            FishboneSymbolKind.Value or FishboneSymbolKind.Constant => new Fact(symbol.Type, false, true),
            FishboneSymbolKind.Type => new Fact(symbol.Type, true, true),
            _ => null
        } : null;
    }

    // --------------------------------------------------------------------------------
    // types
    // --------------------------------------------------------------------------------

    private static Fact Exact(Type type) => new(type, false, true);

    // a member or return type. the value can be of a derived type, and a nullable arrives as its
    // underlying type or null
    private static Fact? Declared(Type type) =>
        type.ContainsGenericParameters || type.IsByRef ? null : new Fact(Nullable.GetUnderlyingType(type) ?? type, false, false);

    // identifiers inside an expression are looked up at the expression's position. nothing can
    // declare a name halfway through an expression, except out arguments, which count as assigned
    private Fact? TypeOf(AstNode node, long at) => node switch
    {
        LiteralNode { Value: { } value } => Exact(value.GetType()),
        InterpolatedStringNode => Exact(typeof(string)),
        ListNode => Exact(typeof(List<object?>)),
        DictionaryNode => Exact(typeof(Dictionary<object, object?>)),
        IdentifierNode identifier => Lookup(identifier.Name, at),
        UnaryOpNode unary => Unary(unary, at),
        BinaryOpNode binary => Binary(binary, at),
        CastNode cast => Cast(cast, at),
        CallNode call => Call(call, at),
        MemberAccessNode member => Member(member, at),
        _ => null
    };

    private static bool IsNumber(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(double);

    private Fact? Unary(UnaryOpNode unary, long at)
    {
        if (unary.Operator == "not")
            return Exact(typeof(bool));
        return TypeOf(unary.Right, at) is { IsStatic: false } right && IsNumber(right.Type) ? Exact(right.Type) : null;
    }

    private Fact? Binary(BinaryOpNode binary, long at)
    {
        if (binary.Operator is "and" or "or" or "xor" or "==" or "!=")
            return Exact(typeof(bool));
        if (TypeOf(binary.Left, at) is not { IsStatic: false } left || TypeOf(binary.Right, at) is not { IsStatic: false } right)
            return null;

        if (binary.Operator == "+" && (left.Type == typeof(string) || right.Type == typeof(string)))
            return Exact(typeof(string));
        if (!IsNumber(left.Type) || !IsNumber(right.Type))
            return null;

        // the widest of int, long and double wins, and '/' is always true division
        bool isDouble = left.Type == typeof(double) || right.Type == typeof(double);
        bool isLong = left.Type == typeof(long) || right.Type == typeof(long);
        return binary.Operator switch
        {
            "<" or ">" or "<=" or ">=" => Exact(typeof(bool)),
            "/" => Exact(typeof(double)),
            "+" or "-" or "*" or "%" => Exact(isDouble ? typeof(double) : isLong ? typeof(long) : typeof(int)),
            _ => null
        };
    }

    // a cast gives the target type, or null when a number doesn't fit
    private Fact? Cast(CastNode cast, long at)
    {
        var baseName = cast.TypeName.TrimEnd('[', ']');
        if (Lookup(baseName, at) is not { IsStatic: true } target)
            return null;
        var type = target.Type;
        for (int i = 0; i < (cast.TypeName.Length - baseName.Length) / 2; i++)
            type = type.MakeArrayType();
        return Declared(type);
    }

    private Fact? Call(CallNode call, long at)
    {
        if (call.Callee is MemberAccessNode member)
        {
            if (TypeOf(member.Target, at) is not { } target)
                return null;
            var methods = ReflectionCache.ResolveMember(target.Type, member.MemberName, target.IsStatic).Methods;
            return methods is null ? null : Returned(methods.Select(method => method.ReturnType));
        }

        if (call.Callee is not IdentifierNode identifier)
            return null;

        // calling a type constructs exactly that type
        if (Lookup(identifier.Name, at) is { IsStatic: true } type)
            return Exact(type.Type);

        // a script function or variable hides the configuration's function
        if (IsDeclared(identifier.Name, Innermost(at)) || _assigned.Contains(identifier.Name)
            || !_symbols.TryGetValue(identifier.Name, out var symbol) || symbol.Kind != FishboneSymbolKind.Function)
            return null;
        return Returned(symbol.Signatures.Select(signature => signature.ReturnType));
    }

    private static bool IsDeclared(string name, Scope? scope)
    {
        for (; scope is not null; scope = scope.Parent)
            if (scope.Declarations.Any(declaration => declaration.Name == name))
                return true;
        return false;
    }

    // a call's type is known when every overload returns the same type, and no converter
    // can change the value on the way back
    private Fact? Returned(IEnumerable<Type> returnTypes)
    {
        var types = returnTypes.Distinct().ToArray();
        if (types.Length != 1 || types[0] == typeof(void))
            return null;
        if (_description.ConvertedTypes.Any(converted => types[0].IsAssignableFrom(converted)))
            return null;
        return Declared(types[0]);
    }

    private Fact? Member(MemberAccessNode member, long at)
    {
        if (TypeOf(member.Target, at) is not { } target)
            return null;
        var lookup = ReflectionCache.ResolveMember(target.Type, member.MemberName, target.IsStatic);
        if (lookup.Property is { } property)
            return Declared(property.PropertyType);
        if (lookup.Field is { } field)
            return Declared(field.FieldType);
        return null;
    }

    // the loop variable is int when every bound is an int, long when they're all integers,
    // and double when any bound is a double
    private Fact? LoopVariable(ForNode loop, long at)
    {
        var bounds = new[] { loop.Start, loop.End, loop.Step }.OfType<AstNode>().Select(bound => TypeOf(bound, at)).ToArray();
        if (bounds.Any(bound => bound is not { IsStatic: false } known || !IsNumber(known.Type)))
            return null;
        var types = bounds.Select(bound => bound!.Value.Type).ToArray();
        return Exact(types.Contains(typeof(double)) ? typeof(double) : types.All(type => type == typeof(int)) ? typeof(int) : typeof(long));
    }

    // --------------------------------------------------------------------------------
    // diagnostics
    // --------------------------------------------------------------------------------

    // a missing member is only reported when the value can't be of a derived type that has it
    private void Check(AstNode node)
    {
        if (node is MemberAccessNode member
            && TypeOf(member.Target, Start(member)) is { } target
            && (target.Exact || target.IsStatic || target.Type.IsSealed || target.Type.IsValueType)
            && ReflectionCache.ResolveMember(target.Type, member.MemberName, target.IsStatic) == MemberLookup.None)
        {
            int endLine = member.EndLine == 0 ? member.Line : member.EndLine;
            int endColumn = member.EndLine == 0 ? member.Column + 1 : member.EndColumn;
            _diagnostics.Add(new(member.Line, member.Column, endLine, endColumn,
                $"Type '{target.Type.Name}' does not have a public member named '{member.MemberName}'."));
        }

        foreach (var child in Children(node))
            Check(child);
    }

    internal static IEnumerable<AstNode> Children(AstNode node) => node switch
    {
        ProgramNode program => program.Statements,
        BlockNode block => block.Statements,
        DeclarationNode declaration => [declaration.Value],
        AssignmentNode assignment => [assignment.Value],
        IndexedAssignmentNode indexed => [indexed.Target, indexed.Index, indexed.Value],
        MemberAssignmentNode assignment => [assignment.Target, assignment.Value],
        UnaryOpNode unary => [unary.Right],
        BinaryOpNode binary => [binary.Left, binary.Right],
        CastNode cast => [cast.Value],
        InterpolatedStringNode interpolated => interpolated.Parts,
        ListNode list => list.Elements,
        DictionaryNode dictionary => dictionary.Pairs.SelectMany(pair => new[] { pair.Key, pair.Value }),
        IndexingNode indexing => [indexing.Target, indexing.Index],
        MemberAccessNode member => [member.Target],
        CallNode call => call.Arguments.Select(argument => argument.Value).Prepend(call.Callee),
        IfNode ifNode => new[] { ifNode.Condition, ifNode.ThenBranch, ifNode.ElseBranch }.OfType<AstNode>(),
        WhileNode loop => [loop.Condition, loop.Body],
        ForeachNode loop => [loop.Iterable, loop.Body],
        ForNode loop => new[] { loop.Start, loop.End, loop.Step, loop.Body }.OfType<AstNode>(),
        FunctionDefinitionNode function => [function.Body],
        ReturnNode { ReturnValue: { } value } => [value],
        ThrowNode { Value: { } value } => [value],
        TryNode tryNode => new AstNode?[] { tryNode.TryBlock, tryNode.CatchBlock, tryNode.FinallyBlock }.OfType<AstNode>(),
        _ => []
    };
}
