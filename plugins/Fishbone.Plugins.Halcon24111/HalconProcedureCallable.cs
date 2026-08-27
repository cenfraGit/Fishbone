using System.Xml.Linq;
using Fishbone.Interpreter;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

internal sealed class HalconProcedureCallable : IManualCallable
{
    private readonly HDevProcedure _procedure;

    private HalconProcedureCallable(HDevProcedure procedure, IReadOnlyList<CallableParameter> parameters)
    {
        _procedure = procedure;
        Parameters = parameters;
    }

    public IReadOnlyList<CallableParameter> Parameters { get; }

    public static HalconProcedureCallable Load(string procedureName, string hdvpPath)
    {
        var procedure = new HDevProcedure(procedureName);
        var parameters = ReadInterface(hdvpPath);
        return new HalconProcedureCallable(procedure, parameters);
    }

    public object? Invoke(object?[] arguments)
    {
        // a fresh call instance per invocation so concurrent or repeated calls never share the
        // procedure's mutable input/output state
        var call = new HDevProcedureCall(_procedure);

        for (int i = 0; i < Parameters.Count; i++)
        {
            var parameter = Parameters[i];
            if (parameter.Direction != ParameterDirection.In)
                continue;

            if (parameter.Type == typeof(HObject))
                call.SetInputIconicParamObject(parameter.Name, (HObject)arguments[i]!);
            else
                call.SetInputCtrlParamTuple(parameter.Name, (HTuple)arguments[i]!);
        }

        call.Execute();

        for (int i = 0; i < Parameters.Count; i++)
        {
            var parameter = Parameters[i];
            if (parameter.Direction != ParameterDirection.Out)
                continue;

            arguments[i] = parameter.Type == typeof(HObject)
                ? call.GetOutputIconicParamObject(parameter.Name)
                : call.GetOutputCtrlParamTuple(parameter.Name);
        }

        return null;
    }

    // the four interface buckets in HALCON's canonical order: iconic inputs, iconic outputs,
    // control inputs, control outputs
    private static readonly (string Element, Type Type, ParameterDirection Direction)[] Categories =
    [
        ("io", typeof(HObject), ParameterDirection.In),
        ("oo", typeof(HObject), ParameterDirection.Out),
        ("ic", typeof(HTuple), ParameterDirection.In),
        ("oc", typeof(HTuple), ParameterDirection.Out),
    ];

    // reads the <interface> section of an .hdvp file into an ordered parameter list
    private static IReadOnlyList<CallableParameter> ReadInterface(string hdvpPath)
    {
        var parameters = new List<CallableParameter>();
        var interfaceElement = XElement.Load(hdvpPath).Element("procedure")?.Element("interface");
        if (interfaceElement is null)
            return parameters;

        foreach (var (element, type, direction) in Categories)
        {
            var bucket = interfaceElement.Element(element);
            if (bucket is null)
                continue;

            foreach (var par in bucket.Elements("par"))
            {
                var name = par.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(name))
                    parameters.Add(new CallableParameter(name, type, direction));
            }
        }

        return parameters;
    }
}