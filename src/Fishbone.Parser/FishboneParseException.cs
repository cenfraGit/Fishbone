namespace Fishbone.Parser;

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