# Fishbone: Embedding Quickstart

How to embed Fishbone in a .NET application: run a script, expose your
objects to it, reuse a parsed program across many runs, and optionally
attach a step-debugger.

---

## 1. Run a script (headless): the 2-line path

The default, synchronous, no-debugger path.

```csharp
using Fishbone.Engine;

var config = new FishboneConfiguration();
var env = FishboneEngine.Run("let answer = 6 * 7;", config);

Console.WriteLine(env.GetValue("answer"));   // 42
```

`FishboneEngine.Run` parses the source and executes it, returning the
resulting environment. Reach into it with `env.GetValue("name")` to
pull values out.

---

## 2. Make your application available to scripts

Fishbone's main purpose is to allow you to interface with your objects
naturally. Everything you inject lands in `Variables` or `BuiltIns`,
and the difference is visible in the debugger:

| Method                        | Lands in          | Shows in IDE *Variables*? | Use for                                 |
|-------------------------------|-------------------|---------------------------|-----------------------------------------|
| `AddValue(name, obj)`         | script variables  | yes                       | injected data the script reads/inspects |
| `AddBuiltIn(name, obj)`       | built-ins         | no                        | services / constants                    |
| `AddType<T>()`                | built-ins         | no                        | types the script can construct          |
| `AddFunction(name, delegate)` | ambient built-ins | no                        | callable C# functions                   |

```csharp
var config = new FishboneConfiguration();

// injected data (appears in debugger's variables panel). readable and assignable
config.AddValue("image", currentImage);

// service or object (callable but hidden from variables panel)
config.AddBuiltIn("camera", myCamera);

// a type you can construct (no new keyword, just call constructor)
config.AddType<Point>();

// a callable c# function
config.AddFunction("log", new Action<string>(Console.WriteLine));
```

A script then sees these as ordinary values:

```fishbone
let p = Point(3, 4);          // constructs Point
camera.Focus();               // calls method on the object
log("focused at " + p.X);     // calls the C# delegate
let w = image.Width;          // access field/properties from object
```

---

## 3. Run the same script many times (parse once, execute many)

A scripting host may want to run the same script repeatedly while its
environment variables change. Instead of parsing every time, reuse the
parsed AST:

```csharp
using Fishbone.Engine;

// parse once, the program is immutable and reusable
var program = FishboneProgram.ParseSource(scriptSource);

// or read from file too
// var program = FishboneProgram.ParseFile("script.fb");

foreach (var image in batch)
{
    // a new config per run. the parsed program is shared
    var config = new FishboneConfiguration();
    config.AddValue("image", image);

    var env = program.Run(config); // no re-parse
    Store(env.GetValue("result"));
}
```

- Each `Run` gets its own environment, the program never mutates
- Cache invalidation is yours to manage: you hold the
  `FishboneProgram`, so you reparse only when you change the
  source. There is no hidden engine cache or logic for that
- `FishboneEngine.Run(source, config)` from section 1 is simply
  `FishboneProgram.ParseSource(source).Run(config)`

---

## 4. Run with the debugger / IDE — the 5-line path

The same script, but a real step-debugger (SpineIDE, or any DAP
client) can attach.  All of the TCP + Debug Adapter Protocol +
IDE-launch logic is hidden behind this call:

```csharp
using Fishbone.Engine;
using Fishbone.DebugAdapter; // debug capability

var program = FishboneProgram.ParseSource(scriptSource);

var result = await program.RunDebuggableAsync(config, new FishboneDebugOptions
{
    OpenIde       = true,
    SourceName    = "script.fb",
    AttachTimeout = TimeSpan.FromSeconds(10),   // if nobody attaches in time, run headless instead
});

if (result.DebuggerAttached)
    Console.WriteLine("ran under the debugger");

var env = result.Environment;                   // same FishboneEnvironment as the headless path
```

> The server always opens; `OpenIde` only controls whether we launch
> SpineIDE. Any DAP client may attach within `AttachTimeout`. SpineIDE
> breaks on the first statement when it attaches, so you get control
> before anything runs. The default launcher just passes `--attach
> <port>`.

The synchronous path needs no options object at all.

A `FishboneRunResult` object is returned once execution finishes:

```csharp
public sealed class FishboneRunResult
{
    public FishboneEnvironment? Environment { get; }
    public Exception? Error { get; }
    public bool DebuggerAttached { get; }
    public bool WasCancelled { get; }
}
```

### Locating the IDE

The default behavior looks for SpineIDE via the `SPINEIDE_PATH`
environment variable, then a known relative location, then `PATH`. You
can override it entirely:

```csharp
var options = new FishboneDebugOptions
{
    OpenIde     = true,
    IdeLauncher = endpoint =>
        Process.Start("SpineIDE", $"--attach {endpoint.Port}"),
};
```

If `OpenIde` is true but no IDE can be found or launched, the run
fallbacks to headless (`DebuggerAttached = false`).

---

## 5. Full example

Putting it together: expose your domain, parse once, run per item,
optionally debuggable.

```csharp
using Fishbone.Engine;

public sealed class InspectionScripting
{
    private readonly FishboneProgram _program;

    public InspectionScripting(string scriptSource)
        => _program = FishboneProgram.ParseSource(scriptSource);   // parse once at load

    public InspectionResult Run(Image image, bool debug)
    {
        var config = new FishboneConfiguration();
        config.AddValue("image", image);        // injected value (visible in the debugger)
        config.AddType<Measurement>();          // a type scripts can construct

        FishboneEnvironment env = debug
            ? _program
                .RunDebuggableAsync(config, new FishboneDebugOptions
                {
                    OpenIde       = true,
                    AttachTimeout = TimeSpan.FromSeconds(10),
                })
                .GetAwaiter().GetResult()
                .Environment!
            : _program.Run(config);                             // no reparse

        return new InspectionResult(env.GetValue("result"));
    }
}
```

---

## API summary

| Call                                              | Purpose                                                                                                          |
|---------------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `FishboneEngine.Run(source, config)`              | Run-once, headless, synchronous                                                                                  |
| `FishboneProgram.ParseSource(source)`             | Parse source text once into an immutable, reusable program                                                       |
| `FishboneProgram.ParseFile(path)`                 | Same, reading the file as UTF-8 and defaulting the source name                                                   |
| `program.Run(config)` / `program.Run(config, ct)` | Execute a parsed program (fresh env each call)                                                                   |
| `program.RunDebuggableAsync(config, options)`     | Run with optional debugger/IDE (extension in `Fishbone.DebugAdapter`)                                            |
| `FishboneDebugOptions`                            | Debug-only options: OpenIde, AttachTimeout, IdeLauncher, RedirectOutput, SourceName (in `Fishbone.DebugAdapter`) |
| `FishboneRunResult`                               | Unified debug-run result (in `Fishbone.DebugAdapter`)                                                            |
| `config.AddValue(name, obj)`                      | Inject a script variable (visible in the debugger)                                                               |
| `config.AddBuiltIn(name, obj)`                    | Inject a built-in object/delegate                                                                                |
| `config.AddType<T>()`                             | Make a type constructable                                                                                        |
| `config.AddFunction(name, delegate)`              | Bind a callable function                                                                                         |
