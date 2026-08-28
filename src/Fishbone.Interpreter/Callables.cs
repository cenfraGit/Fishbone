// --------------------------------------------------------------------------------
// Callables.cs
//
// a callable is something you can put parentheses after, being
// constructors, methods, functions, etc.
//
// this file defines the different callable kinds.
// there are different "callable" circumstances in practice:
//
// - FishboneFunction: a fb-script defined function
//
// - BoundMethod: a method from a .NET object, (like "obj.DoSomething()").
//                BoundMethod holds the object and a list of matching methods.
//                this is so the interpreter picks the right overload depending
//                on the number of args and types. supports ref/out.
//
// - RegisteredType: basically a constructor call. if the host did
//                   config.AddType<Point>(), the script could construct the
//                   Point object doing Point(100, 200) for example.
//
// - a delegate: this one doesn't have a type in here. you can just pass it via
//               name as long as there are no overloads for the method (in that
//               case you'd have to wrap it in Func<> or something).
//
// - IManualCallable: this callable is built by hand by the host, not a real .net
//                    delegate. its an option because delegates like Func<> can't have
//                    out/ref parameters. the host can describe the parameters
//                    using CallableParameter, ParameterDirection to define the
//                    list of arguments that this callable will take. this one is
//                    used rarely and for some specific cases
//
// --------------------------------------------------------------------------------

using Fishbone;
using Fishbone.Ast;
using System.Reflection;

namespace Fishbone.Interpreter;

// --------------------------------------------------------------------------------
// FishboneFunction
// --------------------------------------------------------------------------------

// callable for pure fishbone script-defined functions.
internal class FishboneFunction
{
    private readonly FunctionDefinitionNode _definition;
    private readonly FishboneEnvironment _closure;

    public FishboneFunction(FunctionDefinitionNode definition, FishboneEnvironment closure)
    {
        _definition = definition;
        _closure = closure;
    }

    public int Arity => _definition.Parameters.Length;

    public object? Call(FishboneInterpreter interpreter, List<object> arguments)
    {
        // new env for function scope
        var envFunction = new FishboneEnvironment(_closure);

        // bind args to names
        for (int i = 0; i < _definition.Parameters.Length; i++)
            envFunction.Declare(_definition.Parameters[i], arguments[i]);

        interpreter.OnFunctionEnter(_definition.Name, envFunction);
        try
        {
            try
            {
                interpreter.EvaluateBlock(envFunction, _definition.Body);
            }
            catch (ReturnException ret)
            {
                return ret.Value;
            }

            return null!;
        }
        finally
        {
            interpreter.OnFunctionExit(_definition.Name);
        }
    }
}
