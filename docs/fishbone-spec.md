# Fishbone Specification

## Introduction

### What Fishbone is

Fishbone is a scripting language written in C# with .NET interop in
mind. It aims to provide an easy way to interface with .NET objects
dynamically at runtime without the need for
recompilation. Fundamentally, the Fishbone runtime is just plain .NET
with little to no runtime behavior variations.

Fishbone doesn't necessarily need to interface with .NET types, as it
can be used to add a simple scripting layer to existing .NET
applications. But even in those cases, the runtime behavior of
Fishbone is mostly defined by the .NET runtime. Note that Fishbone is
**not a sandbox** — see the [Security](#security) section.

### What Fishbone is not

Fishbone is not a "Python/Lua/Javascript" for .NET. Do not expect
similar behaviors to any of those languages. It is also not a language
that is expected to exist outside of .NET; Fishbone's entire purpose
is to interface with .NET types at runtime. That "interfacing" lies on
the fact that Fishbone's interpreter is written in C#, and its runtime
deliberately uses .NET types directly without trying to wrap them.

Fishbone is also **not** a standalone CLR language, nor does it
compile to MSIL or run on the DLR.

## Lexical structure

A Fishbone source file consists of UTF-8 encoded text. The parser
skips spaces (`\u0020`), tabs (`\u0009`), line feed (`\u000A`) and
carriage return (`\u000D`).

### Comments

Comments are used to either document the code or to disable sections
of it. There are two ways of declaring comments:

- Line comments: start with `//` and encompass everything until line
  feed or carriage return
- Block comments: start with `/*`, end with `*/`, and encompass
  everything within it

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

An identifier may reference a variable or a function. It consists of
one or more characters, where the first character must be a letter
(`[a-zA-Z]`) and the rest of characters can be either a letter, a
number, or underscore (`[a-zA-Z0-9_]*`).

### Reserved keywords

An identifier's name must also not collide with the reserved keywords,
which include:

- `let`
- `null`
- `true`
- `false`
- `if`
- `else`
- `while`
- `foreach`
- `for`
- `break`
- `continue`
- `try`
- `catch`
- `finally`
- `throw`
- `in`
- `as`
- `func`
- `return`
- `and`
- `or`
- `xor`
- `not`
- `out`
- `ref`

### Operators and punctuation

| Token(s)                    | Description                                       |
|-----------------------------|---------------------------------------------------|
| `+` `-` `*` `/` `%`         | Arithmetic operators                              |
| `==` `!=` `<` `>` `<=` `>=` | Comparison operators                              |
| `and` `or` `xor` `not`      | Boolean operators                                 |
| `=`                         | Assignment                                        |
| `+=` `-=` `*=` `/=` `%=`    | Compound assignment                               |
| `.`                         | Member access                                     |
| `[` `]`                     | Indexing / list and dictionary construction       |
| `(` `)`                     | Grouping / call expressions                       |
| `{` `}`                     | Block delimiters                                  |
| `;`                         | Statement terminator                              |
| `:`                         | Key-value separator in dictionary literals        |
| `,`                         | Separator in lists, dictionaries, parameters, and arguments |

### Literals

Literals in Fishbone are string representations of a value in the
source code. Fishbone supports integer, double, string, boolean, and
null literals.

#### Integer literals

Integer literals support underscores to aid readability (underscores
are removed by the parser). Here are some examples of valid integers:

```csharp
// 1
// 32
// 1_000_000
```

Like C#, an integer literal has the smallest integer type that fits
its value: `int` (32-bit), then `long` (64-bit). A literal too large
for a `long` is a parse error.

Exponent notation always produces a `double`, never an integer, even
when the value is mathematically integral. See Double literals.

### Double literals

A double literal has an optional integer part, an optional fractional
part introduced by a decimal point, and an optional exponent
introduced by `e` or `E`. At least one of the fractional part or the
exponent must be present, and when a decimal point is written, at
least one digit must follow it.

The exponent may carry an explicit `+` or `-` sign, and must be
followed by at least one digit, so `1e` and `1e+` are parse errors.
Unlike integer literals, double literals do not permit underscore
separators. Here are some examples:

```csharp
// 1.0
// 714.000
// 3.141592
// .5
// 1e10
// 2.5e-3
// 1.5E+7
// 6.022e23
// .5e3
```

A literal whose magnitude exceeds the range of a 64-bit double, such
as `1e400`, is a parse error. A literal that underflows, such as
`1e-400`, evaluates to `0`.

### String literals

String literals are enclosed in double quotes. Escape sequences follow
C# conventions. The supported set is `\"`, `\'`, `\\`, `\0`, `\a`,
`\b`, `\f`, `\n`, `\r`, `\t`, `\v`, and `\uXXXX` (four hexadecimal
digits). Any other character after a backslash is a parse error, as is
a literal (unescaped) line break inside a string.

#### Raw (verbatim) strings

A string prefixed with `@` is taken verbatim: backslashes are ordinary
characters, a doubled quote (`""`) produces one literal quote, and the
string may span multiple lines.

```csharp
let path = @"C:\Users\me\file.txt";
let quoted = @"she said ""hi""";
```

#### Interpolated strings

A string prefixed with `$` may embed expressions in `{ }` holes. Each
hole holds a full Fishbone expression; `{{` and `}}` produce literal
braces. Escape sequences work as in regular strings.

```csharp
let msg = $"hello {name}, next year you are {age + 1}";
let entry = $"value: {d["key"]}";
let braces = $"{{literal braces}}";
```

Hole values are converted to text with the invariant culture; `null`
produces an empty string. Unlike C#, format specifiers and alignment
(`{x:F2}`, `{x,10}`) are not supported (a hole is always a plain
expression) and the combined `$@"..."` form is not available.

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

| Type         | Examples                | Notes                                                 |
|--------------|-------------------------|-------------------------------------------------------|
| `int`        | `42`, `-1`, `1_000_000` | 32-bit signed integer (wraps on overflow)             |
| `long`       | `999_999_999_999`       | 64-bit signed integer; literals too large for `int` promote to `long` |
| `double`     | `3.14`, `.5`, `1e10`, `2.5e-3` | 64-bit double-precision float                  |
| `string`     | `"hello"`, `""`         | Unicode text                                          |
| `bool`       | `true`, `false`         |                                                       |
| `null`       | `null`                  | Represents the absence of a value                     |
| `list`       | `[1, 2, 3]`             | Ordered, mutable collection                           |
| `dictionary` | `{"x": 1, "y": 2}`      | Key-value collection. Keys and values can be any type |
| function     | `func f(x) { ... }`     | First-class closure                                   |
| .NET object  | any CLR type            | See Interop section                                   |

### Truthiness

When a value is used in a boolean context (`if`, `while`, `and`, `or`,
`not`), it is considered truthy or falsy as follows:

- `null` is falsy
- `bool` is its own value
- `int` is falsy if zero, truthy otherwise
- `double` is falsy if zero, truthy otherwise
- `string` is falsy if empty, truthy otherwise
- Everything else is truthy

## Blocks

A block is a sequence of zero or more statements enclosed in `{`
`}`. Blocks create a new lexical scope.

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
- Assignment (`x = ...`) walks up the scope chain to find an existing
  binding and updates it. If no binding is found, an error is raised.
- Each block `{ }` creates a child scope.
- Functions close over their definition environment.
- Variables declared in an outer scope are visble and can be shadowed
  by a new `let` declaration.

## Expressions

Fishbone supports the following expression forms:

| Expression     | Syntax                                                       | Description                                        |
|----------------|--------------------------------------------------------------|----------------------------------------------------|
| Literal        | `42`, `"hello"`, `true`                                      | Integer, double, string, bool, null                |
| Identifier     | `x`, `myVar`                                                 | Reference to a variable or function                |
| Parenthesized  | `( expr )`                                                   | Explicit grouping                                  |
| Unary          | `- expr`, `not expr`                                         | Numeric negation, boolean negation                 |
| Multiplicative | `expr * expr`, `expr / expr`, `expr % expr`                  | `int / int` returns `double`; `%` is the remainder |
| Additive       | `expr + expr`, `expr - expr`                                 | `+` also concatenates strings                      |
| Cast           | `expr as identifier`                                         | Safe conversion; `null` when not convertible       |
| Comparison     | `expr < expr`, `expr > expr`, `expr <= expr`, `expr >= expr` | Returns `bool`                                     |
| Equality       | `expr == expr`, `expr != expr`                               | Returns `bool`                                     |
| Boolean        | `expr and expr`, `expr or expr`, `expr xor expr`             | Short-circuiting `and`/`or`                        |
| List           | `[ expr , expr , ... ]`                                      | Creates a list                                     |
| Dictionary     | `{ key : value , ... }`                                      | Creates a dictionary                               |
| Call           | `expr ( expr , ... )`                                        | Function/method call                               |
| Member access  | `expr . identifier`                                          | Access .NET property, field, or method group       |
| Indexing       | `expr [ expr ]`                                              | List index, dictionary key, or .NET indexer        |

Operator precedence, from highest to lowest:

1. Unary (`-`, `not`)
2. Multiplicative (`*`, `/`, `%`)
3. Additive (`+`, `-`)
4. Cast (`as`)
5. Comparison (`<`, `>`, `<=`, `>=`)
6. Equality (`==`, `!=`)
7. Boolean (`and`, `or`, `xor`)

### Arithmetic semantics

- `+`, `-`, `*` preserve `int` when both operands are `int`, and
  produce a `double` when either operand is a `double`.
- `/` is true division: it always produces a `double`, regardless of
  operand types, so `5 / 2` is `2.5` and `4 / 2` is `2.0`. Integer
  division by zero therefore yields `double` infinity rather than an
  error. There is no dedicated floor-division operator; use `int(a /
  b)` when an integer quotient is required.
- `%` is the remainder operator. It preserves `int` when both operands
  are `int` (only `/` promotes to `double`), and follows the C#
  truncated convention where the sign of the result follows the
  dividend: `-5 % 3` is `-2` and `5 % -3` is `2`. Integer remainder by
  zero raises an error; `double` remainder by zero yields `NaN`.

### Equality and comparison semantics

- `==` and `!=` are **total**: they never raise an error, whatever the
  operand types. Numbers compare by value across `int`/`double` (`1 ==
  1.0` is `true`); everything else uses value equality, which means
  operands of different or otherwise incompatible types are simply not
  equal (`1 == "1"` is `false`, not an error). Equality on .NET
  objects honors that type's own `Equals` (so records and other
  value-equal types compare by value); a type that does not define
  equality falls back to reference identity.
- `<`, `>`, `<=`, `>=` require operands that can be ordered (the
  numeric types, or any .NET type that defines the relevant
  comparison). Unlike equality, comparing values that have no ordering
  relationship — for example a number and a string — raises an error
  rather than returning a result, because there is no meaningful
  answer.

### Cast expressions (`as`)

`expr as TypeName` is a **safe cast**: it evaluates to the value
converted to the named type, or `null` when the conversion is not
possible. It never raises an error for a failed conversion (only for
an unknown type name).

```csharp
let n = "42" as int;       // 42
let bad = "oops" as int;   // null
let p = value as Point;    // the same instance if value is a Point, else null
let x = null as int;       // null
```

The type name is resolved at runtime, in order:

1. A registered type — anything the host exposed through
   `AddType<T>()` (or any environment value that is a .NET
   `System.Type`).
2. The built-in primitive names `int`, `double`, `string`, `bool`.
   (These names normally resolve to the conversion *functions*, so
   they are special-cased as cast targets.)

If the name matches neither, the cast raises a runtime error.

Conversion uses the same rules as .NET method-argument interop: if
the value is already an instance of the target type it is returned
unchanged; otherwise a host-registered `TypeConverter` for the target
type is tried, then enum conversion, then `Convert.ChangeType` (with
the invariant culture) for `IConvertible` values. Note that numeric
conversion follows .NET rounding (`3.7 as int` is `4`), unlike a C#
cast which truncates.

`as` differs from the conversion builtins (`int(x)`, `double(x)`,
`string(x)`) in its failure mode: the builtins return a default value
(`0`, `0.0`, `""`) when the conversion fails, while `as` returns
`null` so the failure is observable.

## Statements

Fishbone programs are sequences of statements. Each statement ends
with a semicolon (`;`), except block statements and control flow
bodies.

### Variable declaration/definition

```csharp
let x = 42;
```

Declares exactly one variable. The value on the right is bound whole,
so a list stays a list.

### Assignment

```csharp
x = 10;
```

Updates an existing variable. Assignment walks up the scope chain to
find the binding.

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

The compound assignment operators `+=`, `-=`, `*=`, `/=`, `%=` are
syntactic sugar. `target op= value` is exactly equivalent to `target =
target op value`, and the result follows the same arithmetic semantics
as the underlying operator (for example `x /= 2` always produces a
`double`). The target must be a variable or an indexed target; any
other target is a parse error.

For an indexed target such as `list[i] += 1`, the index expression is
evaluated twice — once to read the current value and once to write the
result. Avoid index expressions with side effects in a compound
assignment.

### Expression statement

```csharp
42;
println("hello");
```

### Statement bodies

The body of an `if`, `else`, `while`, `foreach`, or `for` is a single
statement. That statement is usually a `{ }` block, but the braces may
be omitted when the body is one statement:

```csharp
if (x > 0)
    println("positive");
```

A single-statement body behaves exactly like a one-statement block: a
`let` inside it is scoped to the body. An `else` binds to the nearest
unmatched `if`.

### If

```csharp
if (expr) { }
if (expr) { } else { }
if (expr) { } else if (expr) { } else { }
```

(`else if` is simply an `else` whose statement is another `if`.)

### While

```csharp
while (expr) { }
```

### Foreach

```csharp
foreach (item in collection) { }
```

Iterates over a list, dictionary (iterates keys), or any .NET
`IEnumerable`.

### For

```csharp
for (i in 0, 10) { }       // i = 0, 1, ..., 9
for (i in 0, 10, 2) { }    // i = 0, 2, 4, 6, 8
for (i in 10, 0) { }       // i = 10, 9, ..., 1
for (i in 10, 0, -2) { }   // i = 10, 8, 6, 4, 2
```

Iterates over a numeric range. The syntax is `for (identifier in
start, end)` or `for (identifier in start, end, step)`. The step
defaults to `1` or `-1` depending on direction. The range is exclusive
of `end`. The loop variable is scoped to the loop body.

### Break / Continue

```csharp
break;
continue;
```

`break` exits the innermost loop. `continue` skips to the next
iteration.

### Return

```csharp
return;
return expr;
```

Exits the current Fishbone function, yielding exactly one value. A
bare `return;` yields `null`. The value travels as-is, so
`return [1, 2];` gives the caller that same two-element list.

### Try / Catch / Finally / Throw

```csharp
try { } catch { }
try { } catch (e) { }
try { } finally { }
try { } catch (e) { } finally { }
throw expr;
throw;      // rethrow, only valid inside a catch block
```

A `try` statement requires at least one of `catch`/`finally`, and the
blocks require braces. There is a single, untyped `catch` clause; the
optional `(name)` binds the exception for the catch block's scope.

Because the Fishbone runtime is .NET's, the caught value **is the
actual .NET exception object**, inspect it with ordinary member
access (`e.Message`, `e.GetType().Name`, `e.InnerException`, ...).
There are no typed catch clauses or filters; a script that needs to
discriminate checks the exception itself.

`throw expr` throws the value: if it already is a .NET `Exception` it
is thrown as-is, otherwise it is wrapped in a `FishboneScriptException`
whose `Message` is the value's text and whose `Value` property holds
the original value. A bare `throw;` rethrows the exception bound by
the nearest enclosing catch.

Not catchable by a script: host cancellation and the internal
control-flow signals — `return`, `break`, and `continue` inside a
`try` behave normally (and still trigger `finally`) rather than being
intercepted by `catch`.

Debugger note: an exception raised inside a `try` is not reported as
an unhandled runtime error; if the `try` has no `catch`, it is
reported once it escapes the statement ("break on unhandled"
semantics).

### Error types

Every Fishbone error is one of three exception types:

- `FishboneParseException`: the script could not be parsed. Carries
  the list of syntax errors with line/column positions.
- `FishboneRuntimeException`: any error while the script runs,
  carrying the `Line`/`Column` of the failing statement or
  expression. Its `InnerException` tells the two cases apart: **null**
  means the language itself diagnosed the error (undefined variable,
  indexing null, an impossible conversion, ...); **non-null** means a
  .NET call made by the script threw, and the inner exception is that
  original exception. This is the type an embedding host catches.
- `FishboneScriptException`: a script `throw` of a non-exception
  value (see above).

Inside a script `catch (e)`, the binding follows the same split: for
a language-diagnosed error `e` is the `FishboneRuntimeException`
itself (with `Line`/`Column`); for a failed .NET call `e` is the
original exception the call threw.

### Reporting errors to a client

The exception types above are the transport. `FishboneDiagnostic` is
the shape a client renders, and `FishboneDiagnostics.From(exception)`
is the one call that produces it:

```csharp
try
{
    FishboneEngine.Run(source, config);
}
catch (Exception exception)
{
    foreach (var diagnostic in FishboneDiagnostics.From(exception))
        Show(diagnostic.Message, diagnostic.Span);
}
```

`From` accepts any exception, so a client never tests for an exception
type itself. A parse failure yields one diagnostic per syntax error; a
runtime failure yields one; a .NET exception that escaped a host call
yields one with an unknown location.

Each diagnostic carries:

| Member | Meaning |
| --- | --- |
| `Stage` | `Lex`, `Parse`, `Runtime`, or `Configuration` |
| `Severity` | `Error` or `Warning` |
| `Message` | the text to show the user |
| `Span` | where it happened, or `SourceSpan.None` |
| `OffendingText` | the source text at fault, when known |
| `RawMessage` | the underlying tool's wording, when `Message` is a rewrite |

`SourceSpan` is 1-based, and `EndColumn` is exclusive, so a
single-line span's length is `EndColumn - Column`. Check `IsKnown`
before using a position, and `IsSingleLine` before sizing an
underline: some diagnostics know only where they start.

Syntax error messages are rewritten from ANTLR's own wording so that
grammar token names never reach the user. `RawMessage` holds the
original when this happened.

Host setup failures are diagnostics too. `FishbonePluginLoader.Load`
returns the plugins that loaded alongside `Configuration` diagnostics
for those that did not, which a host without a console should render
itself. `LoadPlugins` is the console convenience that writes them to
`Console.Error` instead.

## Functions

### Function declaration

```csharp
func name(param1, param2) {
    statements
}
```

Fishbone functions can be assigned to variables, passed as arguments,
and returned from other functions.

### Parameters and return

- Parameters are passed by value unless the definition marks them `out`
  or `ref` (see By-reference parameters below).
- A function without a `return` statement implicitly returns `null`.
- A function returns exactly one value. To hand back several, use `out`
  parameters, or return a list or a dictionary.

### Closures

Functions close over the environment in which they are defined. Inner
functions can access variables from outer scopes.

### By-reference parameters (`out` / `ref`)

A parameter in a function definition may be marked `out` or `ref`. The
call site must then mark the matching argument with the same keyword,
and that argument must be a plain variable:

```csharp
func tryHalve(n, out half)
{
    if (n % 2 != 0) { return false; }
    half = n / 2;
    return true;
}

let ok = tryHalve(10, out h);   // ok is true, h is 5; 'h' is introduced by the call

func bump(ref n) { n = n + 1; }
let count = 10;
bump(ref count);                // 'count' must already exist; it becomes 11
```

- An `out` parameter starts as `null` inside the function; it does not
  read the caller's variable. Assigning it is not required, so a
  function that never assigns its `out` parameter hands back `null`.
- An `out` argument does not require the caller's variable to exist. If
  it is undefined, the call declares it in the current scope; if it
  already exists, the call writes through to it.
- A `ref` parameter reads the caller's current value on the way in, and
  its final value is written back. The caller's variable must already be
  defined.
- Write-back happens when the function returns normally, with or without
  a `return` value. If the body throws, nothing is written back.
- Omitting the keyword on an `out`/`ref` parameter, using a keyword on a
  by-value parameter, using the wrong keyword, or passing anything other
  than a plain variable with a keyword are all errors.

These are the same rules the `out`/`ref` arguments of .NET methods
follow.

### Arity

The number of arguments at the call site must match the number of
parameters in the definition, and each argument's `out`/`ref` keyword
must match the direction its parameter declares.

## Builtins & interop

### Built-in functions

Fishbone provides the following built-in functions available in every
script:

| Function           | Description                             |
|--------------------|-----------------------------------------|
| `print(value)`     | Prints value without a trailing newline |
| `println(value)`   | Prints value followed by a newline      |
| `input()`          | Reads a line from stdin                 |
| `abs(x)`           | Absolute value                          |
| `round(x, digits)` | Rounds `x` to `digits` decimal places   |
| `min(a, b)`        | Returns the smaller of two values       |
| `max(a, b)`        | Returns the larger of two values        |
| `pow(x, y)`        | `x` raised to the power of `y`          |
| `sqrt(x)`          | Square root                             |
| `int(value)`       | Converts to integer                     |
| `double(value)`    | Converts to double                      |
| `string(value)`    | Converts to string                      |

### Built-in constants

- `PI` — 3.141592653589793
- `E` — 2.718281828459045

### .NET interop

Fishbone can interface with any .NET object at runtime.

**Member access** — The `.` operator accesses properties, fields, and
methods on any .NET object:

```csharp
let list = [1, 2, 3];
let count = list.Count;
```

**Method calls** — Methods are resolved at runtime. When a method has
overloads, Fishbone filters to those whose parameters can accept the
supplied arguments, then selects the *best* match: each argument is
scored by how closely it matches the parameter type — an exact
runtime-type match ranks above a reference/interface assignment (such
as `int` to `object`), which ranks above a value conversion (such as
`int` to `double`, or an enum from a string). The overload with the
highest total score wins. If two overloads tie for the best score, the
one that filled fewer optional parameters from their defaults wins; if
they still tie, the call is rejected as ambiguous rather than silently
choosing one.

**Optional parameters** — Fishbone has no optional parameters of its
own, but when calling a .NET method it may omit trailing arguments
whose parameters declare default values; each omitted parameter is
supplied from its default. Arguments are matched left to right, so
only a contiguous tail may be omitted. Supplying more arguments than
the method has parameters never binds, and a parameter without a
default value must always be given. (`out`/`ref` parameters are never
optional.)

```csharp
// void Canny(InputArray src, OutputArray dst, double t1, double t2, int aperture = 3, bool l2 = false)
canny(src, dst, 100, 200);          // aperture and l2 take their defaults
canny(src, dst, 100, 200, 5);       // aperture = 5, l2 takes its default
```

**Indexing** — The `[ ]` operator works with .NET indexers, `IList`,
and `IDictionary`.

**Type conversions** — When calling .NET methods, Fishbone
automatically converts values via `Convert.ChangeType`. Enum
parameters accept both string names (`"Monday"`) and integer values,
parsed via `Enum.Parse`.

**Custom type converters** — The automatic conversion above only
covers types that are `IConvertible` or enums. For a .NET type that is
neither (a wrapper such as a tuple or matrix type), a host can
register a converter with
`FishboneConfiguration.AddTypeConverter(type, toNet, fromNet?)`. The
`toNet` direction is consulted wherever a value of that type is
expected — by-value, `ref`, and `out` arguments alike — and ranks as
an explicit conversion for overload resolution. The optional `fromNet`
direction normalizes a value of that type back into a script value
when it leaves a call as a return value or is written back through
`out`/`ref`; omitting it leaves such values as opaque .NET
objects. This lets a wrapped type be passed and received with ordinary
script values:

```csharp
// host: config.AddTypeConverter(typeof(MyType),
//           toNet:   v => MyLibraryTypeConverter.ToMyType(v),
//           fromNet: v => MyLibraryTypeConverter.FromMyType((MyType)v));
my_func(some_input, out some_output, 10, 255);        // 10 and 255 convert to MyType on the way in
some_other_func(some_output, out some_other_output);  // out MyType values come back as numbers
```

**Construction** — A host can register a .NET type with
`FishboneConfiguration.AddType<T>()` (optionally under a custom
name). A registered type is bound as a callable whose name acts like a
constructor — there is no `new` keyword:

```csharp
// host: config.AddType<Point>();
let p = Point(3, 4);   // invokes the Point(int, int) constructor
let sum = p.X + p.Y;   // instances are ordinary .NET objects
```

**Static members**: A registered type is also a static scope, the way
a type name is in C#. The same registration that allows construction
also exposes the type's public static methods, properties and fields
through the `.` operator:

```csharp
// host: config.AddType<Point>();
let d = Point.Distance(a, b);   // a public static method

// host: config.AddType(typeof(MathUtils), "mu");
let r = mu.Clamp(value, 0, 10);
```

A type with no public constructor is still worth registering for its
statics alone. Note that a static class cannot be a generic type
argument in C#, so it must be registered through the non-generic
`AddType(Type, string?)` overload.

Static members are read-only from a script: assigning to a member
(`Type.Member = value`) is not supported, for statics or instances.
Methods that the interop path can never invoke (open generic
definitions and anything taking a pointer) are not exposed, so naming
one reports it as absent rather than failing inside reflection.

Constructor overloads are resolved with the same best-match rules as
method calls. Calling a registered type with no matching constructor,
or registering a type that exposes no public constructor, is an error.

**By-reference arguments (`out` / `ref`)** — When a .NET method has
`out` or `ref` parameters, the call site must mark the corresponding
argument with the matching keyword, and the argument must be a plain
variable:

```csharp
// bool TryParse(string text, out int value)
let ok = TryParse("42", out parsed);   // 'parsed' is introduced by the call
// void Increment(ref int value)
let n = 10;
Increment(ref n);                      // 'n' must already exist; it is updated in place
```

- `out` does not require the variable to exist beforehand: if it is
  undefined, the call declares it in the current scope; if it already
  exists, the call writes through to it.
- `ref` requires the variable to already be defined; its current value
  is passed in and the updated value is written back.
- Omitting the keyword on an `out`/`ref` parameter, using a keyword on
  a by-value parameter, or passing a non-variable expression with a
  keyword are all errors. The same rules apply to Fishbone functions
  that declare `out`/`ref` parameters (see Functions).

**Host callables with native signatures (`IManualCallable`)** —
`out`/`ref` are not limited to reflected .NET methods. A host can
expose a callable that is not a .NET method but still declares a typed
`in`/`out`/`ref` signature, by registering an object implementing
`IManualCallable` (its `Parameters` list gives each parameter a name,
.NET type, and direction). The interpreter binds arguments
positionally, converts inputs through the same registered-converter
logic as a .NET call, invokes the host's implementation, and writes
`out`/`ref` results back into the script's variables. This is how
runtime-defined operations that have no backing .NET method are called:

```csharp
my_callable(image, out output1, out output2);  // inputs convert in; outputs come back as script values
```

Because there is a single fixed signature there is no overload
resolution; the argument count must match exactly, and the same
keyword rules as above apply.

### Plugins

External .NET assemblies implementing `IFishbonePlugin` can be loaded
to register custom builtins. Plugins are loaded from the
`.fishbone/plugins/` directory at the user's home directory.
## Security

Fishbone scripts execute **with the full trust of the host process**.
This is a deliberate consequence of the design: because member access
uses reflection on real .NET objects, a script can reach anything the
host itself can reach (from any object, `GetType()` leads to `Type`,
`Assembly`, and the rest of the reflection API). There is no in-process
sandbox, and Fishbone does not claim to provide one. Treat script
authors as having the same privileges as code contributors, and only
run scripts you trust.

For hosts that must run scripts from untrusted authors, the
configuration offers a coarse but effective switch:

```csharp
var config = new FishboneConfiguration { EnableMemberAccess = false };
```

With member access disabled, the `.` operator is rejected at runtime
entirely (no property or field reads and no method calls on any
object) which closes the reflection surface. Scripts are then limited
to host-registered functions, operators, control flow, and
list/dictionary indexing, so the host curates the entire API surface
through `AddFunction`/`AddBuiltIn`. When designing for this mode,
expose helpers for anything scripts would otherwise reach through
members (e.g. a `count(xs)` function instead of `xs.Count`).

Even with member access disabled, remember that every injected
function runs with full host privileges (the security of this mode is
exactly the security of the API you register). Also consider the other
denial-of-service axes for untrusted scripts: pass a
`CancellationToken` with a timeout to bound runaway loops, and be
aware that scripts can allocate (e.g. by growing lists) like any other
code in your process. For true isolation, run scripts in a separate
process.