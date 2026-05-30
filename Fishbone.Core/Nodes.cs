namespace Fishbone.Core;

public abstract record AstNode;

public record BlockNode(List<AstNode> Statements) : AstNode;
public record DeclarationNode(string Name, AstNode Value) : AstNode;
public record AssignmentNode(List<string> Names, AstNode Value) : AstNode;
public record UnaryOpNode(string Operator, AstNode Right) : AstNode;
public record BinaryOpNode(string Operator, AstNode Left, AstNode Right) : AstNode;
public record IdentifierNode(string Name) : AstNode;
public record LiteralNode(object Value) : AstNode;