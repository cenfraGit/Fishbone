using System;
using System.Collections;
using System.Reflection;

namespace Fishbone.Core;

public static class Utils
{
    // mainly used for debugging ast
    public static void PrintAST(this AstNode? node, string indent = "", string label = "")
    {
        if (node == null) return;

        Type type = node.GetType();
        string prefix = string.IsNullOrEmpty(label) ? "" : $"{label} ";

        // node name
        Console.WriteLine($"{indent}{prefix}{type.Name}");

        string childIndent = indent + "    ";

        // public properties of the record
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            if (prop.Name == "EqualityContract") continue;

            object? value = prop.GetValue(node);
            if (value == null) continue;

            // if another ast node
            if (value is AstNode childNode)
                childNode.PrintAST(childIndent, $"[{prop.Name}]");

            // if set of nodes
            else if (value is IEnumerable enumerable && value is not string)
            {
                int index = 0;
                foreach (var item in enumerable)
                    if (item is AstNode listNode)
                        listNode.PrintAST(childIndent, $"[{prop.Name}:{index++}]");
            }
            // primitive value
            else
                Console.WriteLine($"{childIndent}└── {prop.Name}: {value} ({value.GetType().Name})");
        }
    }
}