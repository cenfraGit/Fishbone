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
- `for`
- `break`
- `continue`
- `in`
- `func`
- `return`
- `and`
- `or`
- `xor`
- `not`
- `out`
- `ref`

### Operators and punctuation

| Token(s) | Description |
|----------|-------------|
| `+` `-` `*` `/` `%` | Arithmetic operators |
| `==` `!=` `<` `>` `<=` `>=` | Comparison operators |
| `and` `or` `xor` `not` | Boolean operators |
| `=` | Assignment |
| `+=` `-=` `*=` `/=` `%=` | Compound assignment |
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
| `int` | `42`, `-1`, `1_000_000` | 32-bit signed integer (wraps on overflow) |
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
| Multiplicative | `expr * expr`, `expr / expr`, `expr % expr` | `int / int` returns `double`; `%` is the remainder |
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
2. Multiplicative (`*`, `/`, `%`)
3. Additive (`+`, `-`)
4. Comparison (`<`, `>`, `<=`, `>=`)
5. Equality (`==`, `!=`)
6. Boolean (`and`, `or`, `xor`)

### Arithmetic semantics

- `+`, `-`, `*` preserve `int` when both operands are `int`, and produce a `double` when either operand is a `double`.
- `/` is true division: it always produces a `double`, regardless of operand types, so `5 / 2` is `2.5` and `4 / 2` is `2.0`. Integer division by zero therefore yields `double` infinity rather than an error. There is no dedicated floor-division operator; use `int(a / b)` when an integer quotient is required.
- `%` is the remainder operator. It preserves `int` when both operands are `int` (only `/` promotes to `double`), and follows the C# truncated convention where the sign of the result follows the dividend: `-5 % 3` is `-2` and `5 % -3` is `2`. Integer remainder by zero raises an error; `double` remainder by zero yields `NaN`.

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

### Compound assignment

```csharp
x += 1;
total -= cost;
scaled *= 2;
average /= count;
remainder %= modulus;
list[i] += 1;
dict["key"] *= 2;
```

The compound assignment operators `+=`, `-=`, `*=`, `/=`, `%=` are syntactic sugar. `target op= value` is exactly equivalent to `target = target op value`, and the result follows the same arithmetic semantics as the underlying operator (for example `x /= 2` always produces a `double`). The target must be a variable or an indexed target; any other target is a parse error.

For an indexed target such as `list[i] += 1`, the index expression is evaluated twice — once to read the current value and once to write the result. Avoid index expressions with side effects in a compound assignment.

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

### For

```csharp
for (i in 0, 10) { }       // i = 0, 1, ..., 9
for (i in 0, 10, 2) { }    // i = 0, 2, 4, 6, 8
for (i in 10, 0) { }       // i = 10, 9, ..., 1
for (i in 10, 0, -2) { }   // i = 10, 8, 6, 4, 2
```

Iterates over a numeric range. The syntax is `for (identifier in start, end)` or `for (identifier in start, end, step)`. The step defaults to `1` or `-1` depending on direction. The range is exclusive of `end`. The loop variable is scoped to the loop body.

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

**Method calls** — Methods are resolved at runtime. When a method has overloads, Fishbone first filters to those whose parameter count matches the argument count, then selects the *best* match: each argument is scored by how closely it matches the parameter type — an exact runtime-type match ranks above a reference/interface assignment (such as `int` to `object`), which ranks above a value conversion (such as `int` to `double`, or an enum from a string). The overload with the highest total score wins. If two overloads tie for the best score, the call is rejected as ambiguous rather than silently choosing one.

**Indexing** — The `[ ]` operator works with .NET indexers, `IList`, and `IDictionary`.

**Type conversions** — When calling .NET methods, Fishbone automatically converts values via `Convert.ChangeType`. Enum parameters accept both string names (`"Monday"`) and integer values, parsed via `Enum.Parse`.

**Construction** — A host can register a .NET type with `FishboneConfiguration.RegisterType<T>()` (optionally under a custom name). A registered type is bound as a callable whose name acts like a constructor — there is no `new` keyword:

```csharp
// host: config.RegisterType<Point>();
let p = Point(3, 4);   // invokes the Point(int, int) constructor
let sum = p.X + p.Y;   // instances are ordinary .NET objects
```

Constructor overloads are resolved with the same best-match rules as method calls. Calling a registered type with no matching constructor, or registering a type that exposes no public constructor, is an error.

**By-reference arguments (`out` / `ref`)** — When a .NET method has `out` or `ref` parameters, the call site must mark the corresponding argument with the matching keyword, and the argument must be a plain variable:

```csharp
// bool TryParse(string text, out int value)
let ok = TryParse("42", out parsed);   // 'parsed' is introduced by the call
// void Increment(ref int value)
let n = 10;
Increment(ref n);                      // 'n' must already exist; it is updated in place
```

- `out` does not require the variable to exist beforehand: if it is undefined, the call declares it in the current scope; if it already exists, the call writes through to it.
- `ref` requires the variable to already be defined; its current value is passed in and the updated value is written back.
- Omitting the keyword on an `out`/`ref` parameter, using a keyword on a by-value parameter, passing a non-variable expression with a keyword, or using `out`/`ref` when calling a Fishbone function are all errors.

### Plugins

External .NET assemblies implementing `IFishbonePlugin` can be loaded to register custom builtins. Plugins are loaded from the `.fishbone/plugins/` directory at the user's home directory.