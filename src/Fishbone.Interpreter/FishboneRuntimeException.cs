namespace Fishbone.Interpreter;

public class FishboneRuntimeException : Exception
{
    public int Line { get; }
    public int Column { get; }

    public FishboneRuntimeException(string message, int line, int column, Exception? inner = null)
        : base(message, inner)
    {
        Line = line;
        Column = column;
    }
}