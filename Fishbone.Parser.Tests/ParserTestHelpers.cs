using Fishbone.Core;

namespace Fishbone.Parser.Tests;

internal static class ParserTestHelpers
{
    public static ProgramNode ParseProgram(string code)
    {
        var ast = ASTParser.Parse(code);
        return Assert.IsType<ProgramNode>(ast);
    }
}
