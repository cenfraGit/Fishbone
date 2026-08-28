// --------------------------------------------------------------------------------
// ParseError.cs
//
// one syntax error from a parse, with its position. carried in bulk by
// FishboneParseException so a whole parse can be reported at once.
// --------------------------------------------------------------------------------

namespace Fishbone;

public readonly record struct ParseError(int Line, int Column, string Message, string? OffendingText);
