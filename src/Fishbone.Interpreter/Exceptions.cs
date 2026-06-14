namespace Fishbone.Interpreter;

public class ReturnException(object values) : Exception
{
    public object Values { get; } = values;
}

public class BreakException : Exception { }
public class ContinueException : Exception { }