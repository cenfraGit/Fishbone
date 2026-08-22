using Fishbone.Core;

namespace Fishbone.Parser.Tests;

// positions of nodes inside an interpolation hole. the hole is parsed by a separate sub-parse, so
// its spans only line up with the real file because the sub-parse is told where it starts. these
// pin the cases the existing suite missed: a newline inside a hole, a nested interpolated string,
// and a hole far enough down the file that an off-by-one in the line offset would show.
public class InterpolationSpanTests
{
    private static InterpolatedStringNode Interpolated(string code, int statementIndex = 0)
    {
        var program = Assert.IsType<ProgramNode>(ASTParser.Parse(code));
        var declaration = Assert.IsType<DeclarationNode>(program.Statements[statementIndex]);
        return Assert.IsType<InterpolatedStringNode>(declaration.Value);
    }

    [Fact]
    public void HoleOnTheFirstLine_ReportsItsOwnColumn()
    {
        //              1234567890123
        //              let s = $"a{1};
        var hole = Interpolated("let s = $\"a{1}\";").Parts[1];

        Assert.Equal(1, hole.Line);
        Assert.Equal(13, hole.Column);
    }

    [Fact]
    public void HoleContainingANewline_ReportsBothLines()
    {
        // a newline is legal inside a hole: HOLE's catch-all is ~["{}], which unlike STRING does
        // not exclude \r\n. nothing covered the line tracking through one until now
        var binary = Assert.IsType<BinaryOpNode>(Interpolated("let s = $\"a{1 +\n2}b\";").Parts[1]);

        Assert.Equal(1, binary.Left.Line);
        Assert.Equal(13, binary.Left.Column);
        Assert.Equal(2, binary.Right.Line);
        Assert.Equal(1, binary.Right.Column);
    }

    [Fact]
    public void NestedInterpolatedString_ReportsTheInnerHolesPosition()
    {
        //                        1234567890123456789
        //                        let s = $"o{$"i{x}"}!";
        var outerHole = Interpolated("let x = 1;\nlet s = $\"o{$\"i{x}\"}!\";", statementIndex: 1);
        var inner = Assert.IsType<InterpolatedStringNode>(outerHole.Parts[1]);
        var innerHole = inner.Parts[1];

        Assert.Equal(2, innerHole.Line);
        Assert.Equal(17, innerHole.Column);
    }

    [Fact]
    public void HoleDeepInTheFile_ReportsTheRealLine()
    {
        // the padding this replaced grew with the line number, so an off-by-one in the line
        // offset would only ever show up well down a file
        string padding = string.Concat(Enumerable.Repeat("let a = 1;\n", 49));
        var hole = Interpolated(padding + "let s = $\"v{a}\";", statementIndex: 49).Parts[1];

        Assert.Equal(50, hole.Line);
        Assert.Equal(13, hole.Column);
    }

    [Fact]
    public void SecondHoleOnALine_IsNotOffsetByTheFirst()
    {
        //                       1234567890123456789012
        //                       let s = $"{a} and {b}";
        var parts = Interpolated("let x = 1;\nlet s = $\"{a} and {b}\";", statementIndex: 1).Parts;

        Assert.Equal(2, parts[0].Line);
        Assert.Equal(12, parts[0].Column);   // 'a', just past the '{' at column 11
        Assert.Equal(2, parts[2].Line);
        Assert.Equal(20, parts[2].Column);   // 'b', just past the '{' at column 19
    }

    [Fact]
    public void HoleAfterAMultiLineHole_PicksUpTheShiftedLine()
    {
        // the line carried out of a multi-line hole has to shift everything after it
        var parts = Interpolated("let s = $\"{1 +\n2} then {3}\";").Parts;

        Assert.Equal(1, parts[0].Line);
        var second = parts[2];
        Assert.Equal(2, second.Line);
    }
}