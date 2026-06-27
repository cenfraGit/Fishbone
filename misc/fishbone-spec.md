# Fishbone Specification

## Introduction

### What Fishbone is

Fishbone is a scripting language written in C# with .NET interop in mind. It aims to provide an easy way to interface with .NET objects dynamically at runtime without the need for recompilation. Fundamentally, the Fishbone runtime is just plain .NET with little to no runtime behavior variations.

Fishbone doesn't necessarily need to interface with .NET types, as it can be used to add a simple, sandboxed scripting layer to existing .NET applications. But even in those cases, the runtime behavior of Fishbone is mostly defined by the .NET runtime.

### What Fishbone is not

Fishbone is not a "Python/Lua/Javascript" for .NET. Do not expect similar behaviors to any of those languages. It is also not a language that is expected to exist outside of .NET; Fishbone's entire purpose is to interface with .NET types at runtime. That "interfacing" lies on the fact that Fishbone's interpreter is written in C#, and its runtime deliberately uses .NET types directly without trying to wrap them.

Fishbone is also **not** a standalone CLR language, nor does it compile to MSIL or run on the DLR.

## Lexical structure

A Fishbone source file consists of UTF-8 encoded text. The parser skips spaces (`\u0020`), tabs (`\u0009`), line feed (`\u000A`) and carriage return (`\u000D`).

### Comments

Comments are used to either document the code or to disable sections of it. There are two ways of declaring comments:

- Line comments: start with `//` and encompass everything until line feed or carriage return
- Block comments: start with `/*`, end with `*/`, and encompass everything within it

```csharp
// this is a line comment
```

```csharp
/*
    this is
    a block
    comment
*/
```

### Identifiers

An identifier may reference a variable or a function. It consists of one or more characters, where the first character must be a letter (`[a-zA-Z]`) and the rest of characters can be either a letter, a number, or underscore (`[a-zA-Z0-9_]*`).

### Reserved keywords

An identifier's name must also not collide with the reserved keywords, which include:

- `let`
- `null`
- `true`
- `false`
- `if`
- `else if`
- `else`
- `while`
- `foreach`
- `break`
- `continue`
- `in`
- `func`
- `return`
- `and`
- `or`
- `xor`
- `not`

### Operators and punctuation

| Token(s) | Description |
|----------|-------------|
| `+` `-` `*` `/` | Arithmetic operators |
| `==` `!=` `<` `>` `<=` `>=` | Comparison operators |
| `and` `or` `xor` `not` | Boolean operators |
| `=` | Assignment |
| `.` | Member access |
| `[` `]` | Indexing / list and dictionary construction |
| `(` `)` | Grouping / call expressions |
| `{` `}` | Block delimiters |
| `;` | Statement terminator |
| `:` | Key-value separator in dictionary literals |
| `,` | Separator in lists, parameters, and destructuring |

### Literals

Literals in Fishbone are string representations of a value in the source code. Fishbone supports integer, double, string, boolean, and null literals.

#### Integer literals

Integer literals support underscores to aid readability (underscores are removed by the parser). Here are some examples of valid integers:

```csharp
// 1
// 32
// 1_000_000
```

### Double literals

A double literal consists of an integer part, a decimal point, and a fractional part. The integer part can be omitted, but the decimal point is always required. Here are some examples:

```csharp
// 1.0
// 714.000
// 3.141592
// .5
```

### String literals

String literals are enclosed in double quotes. Escape sequences follow C# conventions (`\n`, `\r`, `\t`, `\\`, `\"`, etc.).

```csharp
// "hello"
// "this is one line \nthis is another line"
// "this is \"also\" another example"
```

### Boolean literals

- `true`
- `false`

### Null literal

The `null` literal simply represents a null reference from .NET.

## Types & values

Fishbone is dynamically typed. Every value is one of the following:

| Type | Examples | Notes |
|------|----------|-------|
| `int` | `42`, `-1`, `1_000_000` | 32-bit signed integer |
| `double` | `3.14`, `.5`, `-2.0` | 64-bit double-precision float |
| `string` | `"hello"`, `""` | Unicode text |
| `bool` | `true`, `false` | |
| `null` | `null` | Represents the absence of a value |
| `list` | `[1, 2, 3]` | Ordered, mutable collection |
| `dictionary` | `{"x": 1, "y": 2}` | Key-value collection. Keys and values can be any type |
| function | `func f(x) { ... }` | First-class closure |
| .NET object | any CLR type | See Interop section |

### Truthiness

When a value is used in a boolean context (`if`, `while`, `and`, `or`, `not`), it is considered truthy or falsy as follows:

- `null` is falsy
- `bool` is its own value
- `int` is falsy if zero, truthy otherwise
- `double` is falsy if zero, truthy otherwise
- `string` is falsy if empty, truthy otherwise
- Everything else is truthy

## Blocks

A block is a sequence of zero or more statements enclosed in `{` `}`. Blocks create a new lexical scope.

```csharp
{
    let x = 1;
    let y = 2;
    x + y
}
```

## Scoping

Fishbone uses lexical scoping.

- `let` declares a new variable in the current block scope.
- Assignment (`x = ...`) walks up the scope chain to find an existing binding and updates it. If no binding is found, an error is raised.
- Each block `{ }` creates a child scope.
- Functions close over their definition environment.
- Variables declared in an outer scope are visble and can be shadowed by a new `let` declaration.

## Expressions

Fishbone supports the following expression forms:

| Expression | Syntax | Description |
|------------|--------|-------------|
| Literal | `42`, `"hello"`, `true` | Integer, double, string, bool, null |
| Identifier | `x`, `myVar` | Reference to a variable or function |
| Parenthesized | `( expr )` | Explicit grouping |
| Unary | `- expr`, `not expr` | Numeric negation, boolean negation |
| Multiplicative | `expr * expr`, `expr / expr` | |
| Additive | `expr + expr`, `expr - expr` | `+` also concatenates strings |
| Comparison | `expr < expr`, `expr > expr`, `expr <= expr`, `expr >= expr` | Returns `bool` |
| Equality | `expr == expr`, `expr != expr` | Returns `bool` |
| Boolean | `expr and expr`, `expr or expr`, `expr xor expr` | Short-circuiting `and`/`or` |
| List | `[ expr , expr , ... ]` | Creates a list |
| Dictionary | `{ key : value , ... }` | Creates a dictionary |
| Call | `expr ( expr , ... )` | Function/method call |
| Member access | `expr . identifier` | Access .NET property, field, or method group |
| Indexing | `expr [ expr ]` | List index, dictionary key, or .NET indexer |

Operator precedence, from highest to lowest:

1. Unary (`-`, `not`)
2. Multiplicative (`*`, `/`)
3. Additive (`+`, `-`)
4. Comparison (`<`, `>`, `<=`, `>=`)
5. Equality (`==`, `!=`)
6. Boolean (`and`, `or`, `xor`)

## Statements

Fishbone programs are sequences of statements. Each statement ends with a semicolon (`;`), except block statements and control flow bodies.

### Variable declaration/definition

```csharp
let x = 42;
let a, b = functionThatReturnsTwoValues();
```

### Assignment

```csharp
x = 10;
a, b = functionThatReturnsTwoValues();
```

Updates an existing variable. Assignment walks up the scope chain to find the binding.

### Indexed assignment

```csharp
list[0] = 10;
dict["key"] = value;
```

Assigns a value to a list index, dictionary key, or .NET indexer.

### Expression statement

```csharp
42;
println("hello");
```

### If

```csharp
if (expr) { }
if (expr) { } else { }
if (expr) { } else if (expr) { } else { }
```

### While

```csharp
while (expr) { }
```

### Foreach

```csharp
foreach (item in collection) { }
```

Iterates over a list, dictionary (iterates keys), or any .NET `IEnumerable`.

### Break / Continue

```csharp
break;
continue;
```

`break` exits the innermost loop. `continue` skips to the next iteration.

### Return

```csharp
return;
return expr;
return expr1, expr2;
```

Exits the current Fishbone function. Single return yields the value. Returning multiple values yields a list of values.

## Functions

### Function declaration

```csharp
func name(param1, param2) {
    statements
}
```

Fishbone functions can be assigned to variables, passed as arguments, and returned from other functions.

### Parameters and return

- Parameters are passed by value.
- A function without a `return` statement implicitly returns `null`.
- Multi-return uses the syntax `return a, b;` and produces a list.
- The caller can destructure the result with `let a, b = func();`.

### Closures

Functions close over the environment in which they are defined. Inner functions can access variables from outer scopes.

### Arity

The number of arguments at the call site must match the number of parameters in the definition.

## Builtins & interop

### Built-in functions

Fishbone provides the following built-in functions available in every script:

| Function | Description |
|----------|-------------|
| `print(value)` | Prints value without a trailing newline |
| `println(value)` | Prints value followed by a newline |
| `input()` | Reads a line from stdin |
| `abs(x)` | Absolute value |
| `round(x, digits)` | Rounds `x` to `digits` decimal places |
| `min(a, b)` | Returns the smaller of two values |
| `max(a, b)` | Returns the larger of two values |
| `pow(x, y)` | `x` raised to the power of `y` |
| `sqrt(x)` | Square root |
| `int(value)` | Converts to integer |
| `double(value)` | Converts to double |
| `string(value)` | Converts to string |

### Built-in constants

- `PI` — 3.141592653589793
- `E` — 2.718281828459045

### .NET interop

Fishbone can interface with any .NET object at runtime.

**Member access** — The `.` operator accesses properties, fields, and methods on any .NET object:

```csharp
let list = [1, 2, 3];
let count = list.Count;
```

**Method calls** — Methods are resolved at runtime. Overload resolution matches arguments by count and type.

**Indexing** — The `[ ]` operator works with .NET indexers, `IList`, and `IDictionary`.

**Type conversions** — When calling .NET methods, Fishbone automatically converts values via `Convert.ChangeType` and enum parsing.

### Plugins

External .NET assemblies implementing `IFishbonePlugin` can be loaded to register custom builtins. Plugins are loaded from the `.fishbone/plugins/` directory at the user's home directory.