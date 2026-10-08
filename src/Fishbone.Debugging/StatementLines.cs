using Fishbone.Ast;

namespace Fishbone.Debugging;

/// <summary>
/// Finds the lines where a statement starts, which are the only lines the debugger stops on.
/// It follows where the interpreter reports statements: each statement of a program or block,
/// and the bodies of ifs, loops, try blocks and functions, with or without braces.
/// </summary>
public static class StatementLines
{
    public static SortedSet<int> Find(AstNode root)
    {
        var lines = new SortedSet<int>();
        Add(root, lines);
        return lines;
    }

    private static void Add(AstNode? node, SortedSet<int> lines)
    {
        switch (node)
        {
            case null:
                return;
            case ProgramNode program:
                foreach (var statement in program.Statements)
                    Add(statement, lines);
                return;
            case BlockNode block:
                foreach (var statement in block.Statements)
                    Add(statement, lines);
                return;
        }

        if (node.Line > 0)
            lines.Add(node.Line);

        switch (node)
        {
            case IfNode ifNode:
                Add(ifNode.ThenBranch, lines);
                Add(ifNode.ElseBranch, lines);
                break;
            case WhileNode whileNode:
                Add(whileNode.Body, lines);
                break;
            case ForeachNode foreachNode:
                Add(foreachNode.Body, lines);
                break;
            case ForNode forNode:
                Add(forNode.Body, lines);
                break;
            case TryNode tryNode:
                Add(tryNode.TryBlock, lines);
                Add(tryNode.CatchBlock, lines);
                Add(tryNode.FinallyBlock, lines);
                break;
            case FunctionDefinitionNode function:
                Add(function.Body, lines);
                break;
        }
    }
}
