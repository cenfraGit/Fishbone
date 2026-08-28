// --------------------------------------------------------------------------------
// FishboneParseException.cs
//
// this one exception is used in the ASTParser only.
//
// var parseTree = parser.program();
// if (errorListener.Errors.Count > 0)
//     throw new FishboneParseException(errorListener.Errors);
//
// so if there's any errors during parsing, ASTParser throws that list
// to whoever called it so that they can report errors to the user.
// --------------------------------------------------------------------------------

namespace Fishbone;

public class FishboneParseException : Exception
{
    public IReadOnlyList<ParseError> Errors { get; }

    public FishboneParseException(IReadOnlyList<ParseError> errors)
        : base(errors.Count == 1
            ? $"Line {errors[0].Line}, column {errors[0].Column}: {errors[0].Message}"
            : $"{errors.Count} syntax errors found")
    {
        Errors = errors;
    }
}