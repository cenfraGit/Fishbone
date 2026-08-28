// --------------------------------------------------------------------------------
// Exceptions.cs
//
// this file holds exception types that are used in control flow during execution.
// these are implemented as exceptions just to break control flow easily.
// --------------------------------------------------------------------------------

namespace Fishbone.Interpreter;

// thrown when a "return" is hit (from fb functions)
internal class ReturnException(object? value) : Exception
{
    public object? Value { get; } = value;
}

// thrown when a "break" is hit
internal class BreakException : Exception { }

// thrown when a "continue" is hit
internal class ContinueException : Exception { }
