# Fishbone Specification

The grammar and semantics of the language. If you want to embed Fishbone in an app instead, read the [embedding quickstart](quickstart.md).

**Contents**

1. [What Fishbone is](#what-fishbone-is)
2. [What Fishbone is not](#what-fishbone-is-not)
3. [Writing a script](#writing-a-script)
4. [Values](#values)
5. [Expressions and operators](#expressions-and-operators)
6. [Statements](#statements)
7. [Functions](#functions)
8. [Errors](#errors)
9. [Talking to .NET](#talking-to-net)
10. [What the host gives you](#what-the-host-gives-you)
11. [Security](#security)

---

## What Fishbone is

Fishbone is a scripting language written in C#, built for .NET interop. It lets you work with .NET objects at runtime without recompiling anything.

The runtime is plain .NET, with little to no behavior of its own. Fishbone values *are* .NET objects, so when a script adds two numbers or calls a method, what happens is whatever C# would have done.

You don't have to use it for interop. It works fine as a plain scripting layer bolted onto an existing app. But even then, its behavior is mostly the .NET runtime's behavior.

Fishbone is **not** a sandbox. See [Security](#security).

## What Fishbone is not

Fishbone is **not** trying to be Python or Lua for .NET. Don't expect any similar behavior to those languages.

It's also not meant to exist outside .NET. Interfacing with .NET types is the entire point, and that interfacing comes from the fact that the interpreter is written in C# and uses .NET types directly instead of wrapping them.

It is not a standalone CLR language either. It does not compile to MSIL and it does not run on the DLR.

---

## Writing a script

A source file is UTF-8 text. The parser skips spaces (`\u0020`), tabs (`\u0009`), line feeds (`\u000A`) and carriage returns (`\u000D`).

### Comments

Two kinds, both like C#:

- Line comments start with `//` and run to the end of the line
- Block comments start with `/*` and end with `*/`

```csharp
// this is a line comment

/*
    this is
    a block
    comment
*/
```

### Identifiers

An identifier names a variable or a function. The first character is a letter or an underscore, and the rest can be letters, digits or underscores (`[a-zA-Z_][a-zA-Z0-9_]*`).

Identifiers are case sensitive, and they can't collide with a reserved keyword or a reserved type name.

### Reserved keywords

There are 25 of them:

`let` `null` `true` `false` `if` `else` `while` `foreach` `for` `break` `continue` `try` `catch` `finally` `throw` `in` `as` `func` `return` `and` `or` `xor` `not` `out` `ref`

### Reserved type names

The C# keyword type names are reserved too. There are 15 of them:

`sbyte` `byte` `short` `ushort` `int` `uint` `long` `ulong` `float` `double` `decimal` `char` `string` `bool` `object`

Each one always means its .NET type, so `int` is `System.Int32`. You can use them as cast targets (`x as int`) and to reach static members (`int.Parse("42")`, see [Static members and enums](#static-members-and-enums)).

You can't declare them as names. `let int = 5;`, `func string() {}`, a parameter, a `for` or `foreach` variable, a `catch` binding, or `out double` are all parse errors. A host can't register them either, so `AddValue("int", ...)`, `AddBuiltIn` and `AddType` with a reserved name throw an `ArgumentException`. Names that only contain one, like `integer` or `Int`, are fine.

### Integer literals

An integer literal may use underscores to stay readable (the parser strips them):

```csharp
// 1
// 32
// 1_000_000
```

Like C#, an integer literal takes the smallest type that fits: `int` (32-bit) first, then `long` (64-bit). A literal too large for a `long` is a parse error.

### Double literals

An integer part, a decimal point, and a fractional part. The integer part is optional, but the decimal point never is:

```csharp
// 1.0
// 714.000
// 3.141592
// .5
```

### String literals

Strings go in double quotes, and escape sequences follow C# conventions. The supported set is `\"`, `\'`, `\\`, `\0`, `\a`, `\b`, `\f`, `\n`, `\r`, `\t`, `\v`, and `\uXXXX` (four hex digits).

Any other character after a backslash is a parse error, and so is an unescaped line break inside a string.

```csharp
// "hello"
// "this is one line \nthis is another line"
// "this is \"also\" another example"
```

#### Raw strings

Prefix a string with `@` and it's taken verbatim. Backslashes are ordinary characters, a doubled quote (`""`) gives you one literal quote, and the string can span lines:

```csharp
let path = @"C:\Users\me\file.txt";
let quoted = @"she said ""hi""";
```

#### Interpolated strings

Prefix a string with `$` and you can embed expressions in `{ }` holes. Each hole holds a full Fishbone expression, and `{{` / `}}` give you literal braces. Escape sequences work the same as in a regular string:

```csharp
let msg = $"hello {name}, next year you are {age + 1}";
let entry = $"value: {d["key"]}";
let braces = $"{{literal braces}}";
```

Hole values are converted to text with the invariant culture, and `null` gives you an empty string.

Two differences from C#: format specifiers and alignment (`{x:F2}`, `{x,10}`) aren't supported, so a hole is always just an expression. And the combined `$@"..."` form doesn't exist.

### Boolean and null literals

`true` and `false`, plus `null`, which is a plain .NET null reference.

---

## Values

Fishbone is dynamically typed. Every value is one of these:

| Type         | Examples                | Notes                                                 |
|--------------|-------------------------|-------------------------------------------------------|
| `int`        | `42`, `-1`, `1_000_000` | 32-bit signed integer. Overflow is an error           |
| `long`       | `999_999_999_999`       | 64-bit signed integer. Literals too big for `int` promote to `long` |
| `double`     | `3.14`, `.5`, `-2.0`    | 64-bit double-precision float                         |
| `string`     | `"hello"`, `""`         | Unicode text                                          |
| `bool`       | `true`, `false`         |                                                       |
| `null`       | `null`                  | The absence of a value                                |
| list         | `[1, 2, 3]`             | Ordered and mutable                                   |
| dictionary   | `{"x": 1, "y": 2}`      | Key-value pairs. Keys and values can be any type      |
| function     | `func f(x) { ... }`     | First-class closure                                   |
| .NET object  | any CLR type            | See [Talking to .NET](#talking-to-net)                |

### Truthiness

When a value lands in a boolean context (`if`, `while`, `and`, `or`, `not`), here's how it's read:

- `null` is falsy
- `bool` is itself
- `int` and `double` are falsy at zero, truthy otherwise
- `string` is falsy when empty, truthy otherwise
- everything else is truthy

---

## Expressions and operators

| Token(s)                    | What it is                                  |
|-----------------------------|---------------------------------------------|
| `+` `-` `*` `/` `%`         | Arithmetic operators                        |
| `==` `!=` `<` `>` `<=` `>=` | Comparison operators                        |
| `and` `or` `xor` `not`      | Boolean operators                           |
| `=`                         | Assignment                                  |
| `+=` `-=` `*=` `/=` `%=`    | Compound assignment                         |
| `.`                         | Member access                               |
| `[` `]`                     | Indexing, and list construction             |
| `(` `)`                     | Grouping, and call expressions              |
| `{` `}`                     | Block delimiters, and dictionary construction |
| `;`                         | Statement terminator                        |
| `:`                         | Key-value separator in a dictionary literal |
| `,`                         | Separator in lists, dictionaries, parameters, arguments, and `for` ranges |

The expression forms:

| Expression     | Syntax                                                       | What it does                                       |
|----------------|--------------------------------------------------------------|----------------------------------------------------|
| Literal        | `42`, `"hello"`, `true`                                      | Integer, double, string, bool, null                |
| Identifier     | `x`, `myVar`                                                 | References a variable or function                  |
| Parenthesized  | `( expr )`                                                   | Explicit grouping                                  |
| Negation       | `- expr`                                                     | Numeric negation                                   |
| Multiplicative | `expr * expr`, `expr / expr`, `expr % expr`                  | `int / int` gives a `double`. `%` is the remainder |
| Additive       | `expr + expr`, `expr - expr`                                 | `+` also concatenates strings                      |
| Cast           | `expr as identifier`                                         | Safe conversion. `null` when it doesn't work       |
| Comparison     | `expr < expr`, `expr > expr`, `expr <= expr`, `expr >= expr` | Returns a `bool`                                   |
| Equality       | `expr == expr`, `expr != expr`                               | Returns a `bool`                                   |
| Not            | `not expr`                                                   | Boolean negation. Binds looser than equality       |
| Boolean        | `expr and expr`, `expr or expr`, `expr xor expr`             | `and` and `or` short-circuit, `xor` can't          |
| List           | `[ expr , expr , ... ]`                                      | Builds a list                                      |
| Dictionary     | `{ key : value , ... }`                                      | Builds a dictionary                                |
| Call           | `expr ( expr , ... )`                                        | Calls a function, method, or registered type       |
| Member access  | `expr . identifier`                                          | Reads a .NET property, field, or method group      |
| Indexing       | `expr [ expr ]`                                              | List index, dictionary key, or .NET indexer        |

Precedence, tightest first:

1. Call, member access, indexing (`f(x)`, `x.y`, `x[i]`)
2. Negation (`-`)
3. Multiplicative (`*`, `/`, `%`)
4. Additive (`+`, `-`)
5. Cast (`as`)
6. Comparison (`<`, `>`, `<=`, `>=`)
7. Equality (`==`, `!=`)
8. `not`
9. `and`
10. `or`, `xor`

Two of those are worth pointing at, because they differ from C#.

**`not` binds loosely.** It sits below equality, not up with `-`. So `not a == b` is `not (a == b)`, and you rarely need parentheses around a comparison you are negating.

**`and` binds tighter than `or` and `xor`**, the same way `*` binds tighter than `+`. So `a or b and c` is `a or (b and c)`. `or` and `xor` share a level and group left to right.

### Arithmetic

- `+`, `-` and `*` keep `int` when both sides are `int`, and give you a `double` as soon as either side is one
- Integer arithmetic is checked, like C# `checked`. If an `int` or `long` result doesn't fit, it's a runtime error instead of silently wrapping around. So `2147483647 + 1` is an error, not `-2147483648`. Cast first when you need a bigger range: `(a as long) * b`. `double` math never errors. A result too big becomes `Infinity`
- `/` is always true division. It gives a `double` no matter what the operands are, so `5 / 2` is `2.5` and `4 / 2` is `2.0`. Integer division by zero therefore produces `double` infinity rather than an error. There's no floor-division operator, so use `a / b as int` when you need an integer quotient. The cast truncates toward zero, like C# integer division
- `%` is the remainder. It keeps `int` when both sides are `int` (only `/` promotes), and follows C#'s truncated convention, where the sign follows the dividend. So `-5 % 3` is `-2` and `5 % -3` is `2`. Integer remainder by zero raises an error, and `double` remainder by zero gives you `NaN`

### Equality and comparison

`==` and `!=` are **total**. They never raise an error, whatever you throw at them. Numbers compare by value across all .NET numeric types (`1 == 1.0` is `true`, and so is `1` against a `float`, `byte` or `decimal` from the host). Integers compare exactly, so two large `long` values that differ are never equal. Only a comparison with a `float` or `double` goes through `double`, like in C#. Everything else uses value equality, so mismatched types are simply not equal. `1 == "1"` is `false`, not an error, and so is `true == 1`, since `bool` and enums aren't numbers. A `char` compares as its character code, like in C#, so `(65 as char) == 65` is `true`.

Dictionary keys don't follow this rule. A dictionary compares keys like a .NET `Dictionary<object, ...>`, so an `int` key and a `double` key are different keys even when `==` says they're equal:

```csharp
let d = {1: "a"};
let x = d[1.0];             // error: the key isn't found
let y = d[(2 / 2) as int];  // "a"
```

This matters most with `/`, which always gives a `double`. Cast the key with `as int` when you look up an integer key with a computed value.

Equality on a .NET object honors that type's own `Equals`, so records and other value-equal types compare by value. A type that doesn't define equality falls back to reference identity.

`<`, `>`, `<=` and `>=` need operands that can actually be ordered: the numeric types, or any .NET type defining the relevant comparison. Unlike equality, comparing two things with no ordering relationship (a number and a string, say) raises an error instead of returning something. There's no meaningful answer to give.

### Casts

`expr as TypeName` is a **safe cast**. It gives you the value converted to the named type, or `null` when the conversion isn't possible. A failed conversion is never an error, though an unknown type name is:

```csharp
let n = 2.7 as int;        // 2
let big = 256 as byte;     // null, out of range
let s = "42" as int;       // null, text is never parsed
let p = value as Point;    // the same instance if value is a Point, otherwise null
let x = null as int;       // null
```

The type name resolves at runtime, in this order:

1. A [reserved type name](#reserved-type-names) like `int` or `double`, which always means its .NET type
2. A registered type, meaning anything the host exposed through `AddType<T>()`, or any environment value that happens to be a .NET `System.Type`

If neither matches, the cast raises a runtime error.

A cast follows C# casts. A value that already is the target type, or derives from it or implements it, comes back unchanged. So `dog as Animal` is the same `dog`, and `cat as Dog` is `null`.

Otherwise the cast first tries the same conversions as a method argument (see [Type conversions](#type-conversions)), including host-registered converters. If those don't apply, numbers get an explicit cast that works like a C# **checked** cast. Where checked C# would throw, `as` gives you `null`:

| Cast                                  | Result     | Why                                                |
|---------------------------------------|------------|----------------------------------------------------|
| `2.7 as int`                          | `2`        | Truncates toward zero, like C#                     |
| `-2.7 as int`                         | `-2`       | Truncates toward zero, like C#                     |
| `65 as char`                          | `'A'`      | C# allows number to `char`                         |
| `3000000000 as int`                   | `null`     | Out of range                                       |
| `256 as byte`, `-1 as uint`           | `null`     | Out of range                                       |
| a `double` too big for `float`        | `Infinity` | Float overflow isn't an error, even in checked C#  |

Checked is deliberate. C#'s default unchecked casts wrap around silently, so `(byte)256` is `0`, and that loses information without telling you.

Anything C# doesn't allow as a cast gives `null`, so `"42" as int`, `42 as string`, `true as int` and `"A" as char` are all `null`. Parsing text is a method call, not a cast: `int.Parse("42")`.

Enum casts only go from an integer to an enum for now (`1 as DayOfWeek`). C# also allows `2.0` to an enum and an enum to `int`, but Fishbone doesn't support those yet.

---

## Statements

A program is a sequence of statements. Each one ends with a semicolon, except blocks and control flow bodies.

### Blocks and scoping

A block is zero or more statements inside `{ }`, and it opens a new scope:

```csharp
{
    let x = 1;
    let y = 2;
    x + y;
}
```

Scoping is lexical:

- `let` declares a new variable in the current scope
- assignment (`x = ...`) walks up the scope chain looking for an existing binding and updates it. If there isn't one, that's an error
- every `{ }` creates a child scope
- functions close over the environment they were defined in
- outer-scope variables are visible, and a new `let` can shadow them

### Declaration and assignment

```csharp
let x = 42;   // declares
x = 10;       // assigns to an existing binding
```

One name per statement. Fishbone has no unpacking, so `let a, b = f();` is a syntax error.

### Indexed assignment

```csharp
list[0] = 10;
dict["key"] = value;
```

Writes to a list index, a dictionary key, or a .NET indexer.

### Member assignment

```csharp
box.Count = 5;
box.Inner.Label = "hi";
Settings.Value = 3;   // a static member, through a registered type
```

Sets a public property or field on a .NET object, or a static one through a type name (see [Static members and enums](#static-members-and-enums)). The value converts the same way as a method argument (see [Type conversions](#type-conversions)), so `box.Count = 2.0` works but `box.Count = 2.5` is an error.

It's an error to assign to a read-only property (no public setter), a `readonly` or `const` field, a method, a missing member, or a member of `null`.

Structs need care. Reading a struct from a member, a call or an index gives you a **copy**, so setting a member on it would change the copy and lose the change. Like C#, that's an error:

```csharp
box.Spot.X = 5;       // error: box.Spot is a copy
let spot = box.Spot;  // copy it into a variable,
spot.X = 5;           // change it there,
box.Spot = spot;      // and assign it back
```

### Compound assignment

```csharp
x += 1;
total -= cost;
scaled *= 2;
average /= count;
remainder %= modulus;
list[i] += 1;
dict["key"] *= 2;
box.Count += 1;
```

`+=`, `-=`, `*=`, `/=` and `%=` are sugar. `target op= value` is exactly `target = target op value`, and the result follows the same arithmetic rules as the operator underneath, so `x /= 2` always produces a `double`.

The target has to be a variable, an indexed target or a member. Anything else is a parse error.

(one caveat with an indexed or member target like `list[i] += 1` or `get().Count += 1`. The target and index expressions are evaluated twice, once to read and once to write, so keep side effects out of them.)

### Expression statements

```csharp
42;
println("hello");
```

### Statement bodies

The body of an `if`, `else`, `while`, `foreach` or `for` is a single statement. Usually that's a `{ }` block, but you can drop the braces for a one-statement body:

```csharp
if (x > 0)
    println("positive");
```

A braceless body behaves exactly like a one-statement block, so a `let` inside it is scoped to the body. An `else` binds to the nearest unmatched `if`.

### If

```csharp
if (expr) { }
if (expr) { } else { }
if (expr) { } else if (expr) { } else { }
```

(`else if` isn't a construct of its own. It's just an `else` whose statement happens to be another `if`.)

### While

```csharp
while (expr) { }
```

### Foreach

```csharp
foreach (item in collection) { }
```

Iterates a list, a dictionary (you get the keys), or any .NET `IEnumerable`.

### For

```csharp
for (i in 0, 10) { }       // i = 0, 1, ..., 9
for (i in 0, 10, 2) { }    // i = 0, 2, 4, 6, 8
for (i in 10, 0) { }       // i = 10, 9, ..., 1
for (i in 10, 0, -2) { }   // i = 10, 8, 6, 4, 2
```

A numeric range. The syntax is `for (identifier in start, end)` or `for (identifier in start, end, step)`. The step defaults to `1` or `-1` depending on which way you're going, `end` is exclusive, and the loop variable is scoped to the body.

### Break and continue

```csharp
break;
continue;
```

`break` leaves the innermost loop, `continue` skips to its next iteration.

### Return

```csharp
return;
return expr;
```

Exits the current Fishbone function. A bare `return;` yields `null`.

Returning more than one value isn't supported, so `return a, b;` is a syntax error. Return a list if you need to hand back several things.

---

## Functions

```csharp
func name(param1, param2) {
    statements
}
```

Functions are first class. You can assign them to variables, pass them as arguments, and return them from other functions.

- Parameters are passed by value
- A function with no `return` implicitly returns `null`
- Functions close over the environment they were defined in, so an inner function can reach outer-scope variables
- The argument count at the call site has to match the parameter count

---

## Errors

### Try, catch, finally, throw

```csharp
try { } catch { }
try { } catch (e) { }
try { } finally { }
try { } catch (e) { } finally { }
throw expr;
throw;      // rethrow, only valid inside a catch block
```

A `try` needs at least one of `catch` or `finally`, and both require braces. There's a single untyped `catch` clause, and the optional `(name)` binds the exception for that block's scope.

Because the runtime is .NET's, the value you catch **is the actual .NET exception object**. Inspect it with ordinary member access:

```csharp
try {
    risky();
} catch (e) {
    println(e.Message);
    println(e.GetType().Name);
}
```

There are no typed catch clauses and no filters, so a script that needs to tell exceptions apart checks the exception itself.

`throw expr` throws the value. If it's already a .NET `Exception` it goes as-is, otherwise it's wrapped in a `FishboneScriptException` whose `Message` is the value's text and whose `Value` property holds the original. A bare `throw;` rethrows whatever the nearest enclosing catch bound.

Two things a script can't catch: host cancellation, and the internal control-flow signals. A `return`, `break` or `continue` inside a `try` behaves normally and still triggers `finally`, rather than being intercepted by `catch`.

(a debugger note. An exception raised inside a `try` isn't reported as an unhandled runtime error. If that `try` has no `catch`, it gets reported once it escapes the statement, which is standard "break on unhandled" behavior.)

### The exception types

A host only ever has to catch two things:

- **`FishboneParseException`**, meaning the script couldn't be parsed. Carries the list of syntax errors with their line and column
- **`FishboneRuntimeException`**, meaning something went wrong while the script ran. Carries the `Line` and `Column` of the failing statement or expression

There's a third type, **`FishboneScriptException`**, for when a script `throw`s a value that isn't already a .NET exception. It **derives from `FishboneRuntimeException`**, so a host that doesn't care why the script failed catches one type and is done. Narrow to it only when you want the thrown value, which it keeps in `Value`:

```csharp
try
{
    program.Run(config);
}
catch (FishboneScriptException ex)
{
    Console.WriteLine($"the script threw {ex.Value} at {ex.Line}:{ex.Column}");
}
catch (FishboneRuntimeException ex)
{
    Console.WriteLine($"failed at {ex.Line}:{ex.Column}: {ex.Message}");
}
```

`InnerException` tells you where a `FishboneRuntimeException` came from. **Null** means the language itself diagnosed the problem: an undefined variable, indexing into null, an impossible conversion. **Non-null** means a .NET call the script made threw, and the inner exception is that original exception. (a `FishboneScriptException` always has a null inner, since nothing failed underneath it.)

Inside a script's `catch (e)`, the binding follows the same split. For a language-diagnosed error, `e` is the `FishboneRuntimeException` itself, with its `Line` and `Column`. For a failed .NET call, `e` is the original exception that call threw. For a script `throw`, `e` is the `FishboneScriptException`.

---

## Talking to .NET

This is the part Fishbone exists for. A script can reach any .NET object at runtime.

### Member access

The `.` operator reads properties and fields, and calls methods, on any .NET object:

```csharp
let list = [1, 2, 3];
let count = list.Count;
```

### Static members and enums

On a type name, `.` reaches the type's **static** members instead: static properties, fields (including `const`) and methods. This works for registered types and for [reserved type names](#reserved-type-names):

```csharp
// host: config.AddType<DayOfWeek>();
//       config.AddType(typeof(Path));   // a static class can't be a generic argument
let day = DayOfWeek.Monday;
let isMonday = day == DayOfWeek.Monday;   // true
let n = int.Parse("42");
let ok = int.TryParse(text, out value);
let file = Path.GetFileName("a/b.txt");
```

Enum values are static fields, so this is how a script names one. An enum value compares by value with `==`, passes to a .NET parameter of that enum type as an exact match, and prints as its name. It's never equal to a number, so `DayOfWeek.Monday == 1` is `false`.

Static and instance members stay separate, like in C#. A type name only sees static members, and an instance only sees instance members, so `"abc".Empty` (a static field reached through an instance) and `DayOfWeek.Monday.Monday` are both errors.

A static method can be stored and called later, like any method: `let parse = int.Parse; parse("7")`.

### Method calls

Methods resolve at runtime. When one has overloads, Fishbone first filters to the ones whose parameters could accept your arguments, then picks the *best* match.

Each argument is scored by how closely it matches the parameter type. An exact runtime-type match ranks above a reference or interface assignment (`int` to `object`), which ranks above a value conversion (`int` to `double`, or an integer to an enum). Highest total score wins.

If two overloads tie, the one that filled fewer optional parameters from their defaults wins. If they still tie, the call is rejected as ambiguous rather than silently picking one.

### Optional parameters

Fishbone has no optional parameters of its own. But when you call a .NET method, you may leave off trailing arguments whose parameters declare defaults, and each omitted one is filled from its default.

Arguments match left to right, so only a contiguous tail can be omitted. Passing more arguments than the method has parameters never binds, and a parameter without a default always has to be given. (`out` and `ref` parameters are never optional.)

```csharp
// void Canny(InputArray src, OutputArray dst, double t1, double t2, int aperture = 3, bool l2 = false)
canny(src, dst, 100, 200);          // aperture and l2 take their defaults
canny(src, dst, 100, 200, 5);       // aperture = 5, l2 takes its default
```

### Indexing

`[ ]` works with .NET indexers, `IList`, and `IDictionary`.

### Type conversions

When you call a .NET method, Fishbone converts an argument only when no information is lost. Otherwise the call fails with an error naming the parameter.

- A number converts to another number type when it fits, meaning it's whole for an integer parameter and in range. So `2.0` goes to an `int`, but `2.5` and `3000000000` don't
- A `double` goes to a `float` parameter when it's in range, even though it loses some precision. C# would need a cast here, but .NET APIs often take `float` and scripts only have `double`
- An integer goes to an enum parameter, so `1` is `DayOfWeek.Monday`. Names like `"Monday"` and non-whole numbers don't
- `null` goes to reference types and to nullable value types like `int?`. A plain `int` parameter rejects it
- Nothing converts to or from `string`, `bool` or `char`. `"42"` is not an `int`, and `5` is not a `string`

The same rules apply to constructors, indexers and host callables built by hand. Use `as` when you want a conversion that may lose information, like truncating `2.7` to `2`.

### Custom type converters

That automatic conversion only covers numbers and enums. For a .NET type that's neither (a tuple or matrix wrapper, say), a host can register its own with `FishboneConfiguration.AddTypeConverter(type, toNet, fromNet?)`.

The `toNet` direction is consulted anywhere a value of that type is expected, so by-value, `ref` and `out` arguments alike, and it ranks as an explicit conversion during overload resolution.

The optional `fromNet` direction normalizes a value of that type back into a script value on its way out of a call, whether as a return value or through `out`/`ref`. Leave it off and such values stay opaque .NET objects.

Together they let a wrapped type be passed and received as ordinary script values:

```csharp
// host: config.AddTypeConverter(typeof(MyType),
//           toNet:   v => MyLibraryTypeConverter.ToMyType(v),
//           fromNet: v => MyLibraryTypeConverter.FromMyType((MyType)v));
my_func(some_input, out some_output, 10, 255);        // 10 and 255 convert to MyType on the way in
some_other_func(some_output, out some_other_output);  // out MyType values come back as numbers
```

### Construction

A host registers a .NET type with `FishboneConfiguration.AddType<T>()`, and optionally gives it a custom name. A registered type is bound as a callable whose name acts like a constructor, so there's no `new` keyword:

```csharp
// host: config.AddType<Point>();
let p = Point(3, 4);   // invokes the Point(int, int) constructor
let sum = p.X + p.Y;   // instances are ordinary .NET objects
```

Constructor overloads resolve with the same best-match rules as method calls. Calling a registered type with no matching constructor is an error. That includes types with no public constructor at all, like an enum or a static class. You can still register those to reach their static members.

### Out and ref arguments

When a .NET method has `out` or `ref` parameters, the call site has to mark the matching argument with the same keyword, and that argument has to be a plain variable:

```csharp
// bool TryParse(string text, out int value)
let ok = TryParse("42", out parsed);   // 'parsed' is introduced by the call
// void Increment(ref int value)
let n = 10;
Increment(ref n);                      // 'n' must already exist, and gets updated in place
```

- `out` doesn't need the variable to exist first. If it's undefined the call declares it in the current scope, and if it does exist the call writes through to it
- `ref` needs the variable to already be defined. Its current value goes in, and the updated value comes back out

Four things are errors: omitting the keyword on an `out`/`ref` parameter, using a keyword on a by-value parameter, passing a non-variable expression with a keyword, and using `out`/`ref` when calling a Fishbone function.

### Host callables built by hand

`out` and `ref` aren't limited to reflected .NET methods. A host can expose a callable that isn't a .NET method at all but still declares a typed `in`/`out`/`ref` signature, by registering an object implementing `IManualCallable`. Its `Parameters` list gives each parameter a name, a .NET type and a direction.

The interpreter binds arguments positionally, converts inputs through the same registered-converter logic as a real .NET call, invokes the host's implementation, and writes `out`/`ref` results back into the script's variables. This is how you call a runtime-defined operation that has no backing .NET method:

```csharp
my_callable(image, out output1, out output2);  // inputs convert in, outputs come back as script values
```

There's one fixed signature, so there's no overload resolution. The argument count has to match exactly, and the same keyword rules as above apply.

---

## What the host gives you

**Fishbone ships with no built-ins.** A fresh `FishboneConfiguration` is empty. There is no `println`, no `input`, no `sqrt`, no `PI`. The language gives you syntax, control flow, operators and .NET interop, and nothing else.

Every name a script can see was put there by the host, in one of two ways.

The host registers things directly:

```csharp
config.AddBuiltIn("println", new Action<object>(Console.WriteLine));
config.AddValue("image", currentImage);
config.AddType<Point>();
```

Or it loads a plugin, which is a .NET class implementing `IFishbonePlugin` that does the same registering on the host's behalf:

```csharp
config.AddPlugin(new MathPlugin());   // now the script has PI, sqrt, pow, and the rest
```

Plugins can also be discovered on disk. `FishbonePluginLoader.LoadPlugins` scans a directory (`~/.fishbone/plugins` by default) for DLLs exporting `IFishbonePlugin` types with a parameterless constructor. This is what SpineIDE, SpineCLI and the DAP host all do.

So what a script can actually call depends entirely on who is running it. Under SpineIDE or SpineCLI you get `print`, `println` and `input`, because those hosts register them. Under your own host you get exactly what you registered.

The three plugins in this repo are [Math](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.Math), [OpenCV](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.OpenCV) and [Halcon24111](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.Halcon24111). See the [quickstart](quickstart.md#5-plugins) for how to wire one up, and the [README](../README.md#plugins) for how to write one.

---

## Security

Fishbone scripts run **with the full trust of the host process**.

That's a deliberate consequence of the design. Member access uses reflection on real .NET objects, so a script can reach anything the host can reach. From any object, `GetType()` leads to `Type`, then `Assembly`, then the rest of the reflection API. There is no in-process sandbox and Fishbone doesn't claim to offer one. Treat script authors as having the same privileges as code contributors, and only run scripts you trust.

If you have to run scripts from untrusted authors, the config offers one coarse but effective switch:

```csharp
var config = new FishboneConfiguration { EnableMemberAccess = false };
```

With member access off, the `.` operator is rejected at runtime entirely. No property or field reads, no method calls on any object, which closes the reflection surface. Scripts are then limited to host-registered functions, operators, control flow, and list/dictionary indexing, so you curate the whole API surface through `AddBuiltIn` and `AddValue`.

When you design for this mode, expose a helper for anything a script would otherwise reach through a member. A `count(xs)` function instead of `xs.Count`, for example.

Two things to keep in mind even with member access off:

- Every function you inject still runs with full host privileges. The security of this mode is exactly the security of the API you registered
- Denial of service has other axes. Pass a `CancellationToken` with a timeout to bound a runaway loop, and remember that a script can allocate (by growing a list, say) like any other code in your process

For real isolation, run scripts in a separate process.
