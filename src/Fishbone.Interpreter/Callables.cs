using Fishbone.Core;
using System.Reflection;

namespace Fishbone.Interpreter;

public interface ICallable
{
    int Arity { get; }
    object Call(FishboneInterpreter interpreter, List<object> arguments);
}

public class FishboneFunction : ICallable
{
    private readonly FunctionDefinitionNode _definition;
    private readonly FishboneEnvironment _closure;

    public FishboneFunction(FunctionDefinitionNode definition, FishboneEnvironment closure)
    {
        _definition = definition;
        _closure = closure;
    }

    public int Arity => _definition.Parameters.Length;

    public object Call(FishboneInterpreter interpreter, List<object> arguments)
    {
        // new env for function scope
        var envFunction = new FishboneEnvironment(_closure);

        // bind args to names
        for (int i = 0; i < _definition.Parameters.Length; i++)
            envFunction.Declare(_definition.Parameters[i], arguments[i]);

        // exec function body
        try
        {
            interpreter.EvaluateBlock(envFunction, _definition.Body);
        }
        catch (ReturnException ret)
        {
            return ret.Values is List<object> list && list.Count == 1
                ? list[0]
                : ret.Values;
        }

        return null!;
    }
}

public sealed class BoundMethod
{
    public BoundMethod(object target, IReadOnlyList<MethodInfo> methods)
    {
        Target = target;
        Methods = methods;
    }

    public object Target { get; }
    public IReadOnlyList<MethodInfo> Methods { get; }
}