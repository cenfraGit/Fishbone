# Embedding Quickstart

## 1. Run a script

The short path. Parse and execute in one call:

```csharp
using Fishbone;

var config = new FishboneConfiguration();
var env = FishboneProgram.Run("let answer = 6 * 7;", config);

Console.WriteLine(env.GetValue("answer"));   // 42
```

`FishboneProgram.Run` parses the source, executes it, and hands back the resulting `FishboneEnvironment`. Reach into it with `env.GetValue("name")` to pull values out.

One thing to know up front: **a fresh `FishboneConfiguration` is empty**. There is no `println`, no `input`, no `sqrt`, no `PI`. Fishbone gives your script the language and nothing else, so a script that calls `println("hi")` will fail until you register it yourself:

```csharp
config.AddBuiltIn("println", new Action<object>(Console.WriteLine));
```

(this is deliberate. the host decides what a script can touch, so nothing shows up that you didn't put there. SpineIDE and SpineCLI both register `print`, `println` and `input` for you, which is why the programs in [samples/](../samples/) can use them.)

---

## 2. Make your application available to scripts

This is what Fishbone is for. Everything you inject lands in either `Values` or `BuiltIns`, and the difference shows up in the debugger:

| Method                             | Lands in    | Shows in the IDE's *Variables* panel? | Use for                                 |
|------------------------------------|-------------|---------------------------------------|-----------------------------------------|
| `AddValue(name, obj)`              | variables   | yes                                   | data the script reads, inspects, or reassigns |
| `AddBuiltIn(name, obj)`            | built-ins   | no                                    | functions, services, constants          |
| `AddType<T>()`                     | built-ins   | no                                    | types the script can construct          |
| `AddTypeConverter(type, to, from)` | converters  | no                                    | types the interop path can't convert on its own |
| `AddPlugin(plugin)`                | whatever the plugin adds | depends              | a reusable bundle of the above          |

```csharp
var config = new FishboneConfiguration();

// injected data. readable, assignable, and visible in the variables panel
config.AddValue("image", currentImage);
config.AddValue("camera", myCamera);

// a type the script can construct (no new keyword, just call the constructor)
config.AddType<Point>();

// a C# delegate the script can call like a function
config.AddBuiltIn("log", new Action<string>(Console.WriteLine));
```

A script then sees all of these as ordinary values:

```csharp
let p = Point(3, 4);          // constructs a Point
camera.Focus();               // calls a method on the object
log($"focused at {p.X}");     // calls the C# delegate
let w = image.Width;          // reads a property off the object
```

Every setup method returns the config, so you can chain them if you prefer:

```csharp
var config = new FishboneConfiguration()
    .AddValue("image", currentImage)
    .AddType<Point>()
    .AddBuiltIn("log", new Action<string>(Console.WriteLine));
```

`config.Clone()` gives you an independent copy, which is handy when you want one shared base setup and small per-run tweaks on top of it.

---

## 3. Run the same script many times

A host often runs one script over and over while the data changes. Instead of reparsing every time, build the program once and reuse it:

```csharp
using Fishbone;

// parse once. the program is immutable and reusable
var program = FishboneProgram.FromSourceCode(scriptSource);

// or read it from a file
// var program = FishboneProgram.FromFile("script.fb");

foreach (var image in batch)
{
    // a new config per run. the parsed program is shared
    var config = new FishboneConfiguration();
    config.AddValue("image", image);

    var env = program.Run(config);   // no reparse
    Store(env.GetValue("result"));
}
```

- Every `Run` gets its own fresh environment. The program itself never mutates
- Cache invalidation is yours to manage. You hold the `FishboneProgram`, so you rebuild it only when you change the source. There's no hidden engine cache
- `program.SourceIdentity` is a SHA256 of the source, so you can compare it against a cached one to tell whether your program is stale
- `FishboneProgram.Run(source, config)` from section 1 is just `FishboneProgram.FromSourceCode(source).Run(config)`

The full signature is `Run(config, debugger, cancellationToken)`, and every argument is optional. Pass a `CancellationToken` to bound a script that might loop forever:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var env = program.Run(config, debugger: null, cancellationToken: cts.Token);
```

---

## 4. Run with the debugger

The same script, but a real step debugger (SpineIDE, or any DAP client) can attach. All of the TCP, Debug Adapter Protocol and IDE launching lives behind one call:

```csharp
using Fishbone;
using Fishbone.DebugAdapter;   // the debug capability lives here

var program = FishboneProgram.FromSourceCode(scriptSource);

var result = await program.RunDebuggableAsync(config, new FishboneDebugOptions
{
    OpenIde       = true,
    SourceName    = "script.fb",
    AttachTimeout = TimeSpan.FromSeconds(10),   // nobody attached in time? run headless
});

if (result.DebuggerAttached)
    Console.WriteLine("ran under the debugger");

var env = result.Environment;   // the same FishboneEnvironment as the headless path
```

(the debug server always opens. `OpenIde` only controls whether we also launch SpineIDE for you. Any DAP client may attach within `AttachTimeout`, and the run breaks on the first statement once one does, so you get control before anything executes. This call never hangs waiting for a debugger.)

Unlike `Run`, `RunDebuggableAsync` requires a config. Pass an empty one if you have nothing to inject.

You get a `FishboneRunResult` back once execution finishes:

```csharp
public sealed class FishboneRunResult
{
    public FishboneEnvironment? Environment { get; }
    public Exception? Error { get; }
    public bool DebuggerAttached { get; }
    public bool WasCancelled { get; }
}
```

### The other debug options

| Option           | Default          | What it does                                                    |
|------------------|------------------|-----------------------------------------------------------------|
| `OpenIde`        | `false`          | Launch an IDE pointed at the debug server                        |
| `AttachTimeout`  | 10 seconds       | How long to wait before falling back to a headless run           |
| `SourceName`     | the program's    | Overrides the name shown on the debugger's tab                   |
| `ListenEndpoint` | ephemeral loopback | The endpoint the debug server listens on                       |
| `RedirectOutput` | `true`           | Sends script output to the debug client                          |
| `IdeLauncher`    | finds SpineIDE   | Launches your own client instead                                 |

### Locating the IDE

The easiest way to get SpineIDE onto a machine is the package for your platform:

```
dotnet add package Fishbone.SpineIDE.win-x64 --version 0.1.0-alpha.1
```

That drops the IDE into a `spineide` folder in your build output, which the launcher checks, so `OpenIde = true` then works with no configuration at all. There is one package per platform (`win-x64`, `linux-x64`) because Avalonia's native rendering binaries are platform specific. To leave it out of a particular build, set `FishboneIncludeSpineIde` to `false`.

In full, the launcher looks for an executable named `spineide` in four places, in order:

1. the path in the `SPINEIDE_PATH` environment variable
2. next to the host application
3. the `spineide` folder in the host's output, which is what the package populates
4. anywhere on `PATH`

It passes `--attach <port>`. You can replace the whole thing:

```csharp
var options = new FishboneDebugOptions
{
    OpenIde     = true,
    IdeLauncher = endpoint =>
        Process.Start("spineide", $"--attach {endpoint.Port}"),
};
```

If `OpenIde` is true but no IDE can be found or launched, that isn't fatal. The run still waits out `AttachTimeout` in case something else attaches, then falls back to headless with `DebuggerAttached = false`.

---

## 5. Plugins

A plugin is a reusable bundle of built-ins, values and types. It's a .NET class implementing `IFishbonePlugin`, and all it does is add things to a config you hand it. See the [Plugins section of the README](../README.md#plugins) for how to write one.

There are two ways to get a plugin into your config. Either you construct it yourself:

```csharp
using Fishbone;
using Fishbone.Plugins.Math;

var config = new FishboneConfiguration();
config.AddPlugin(new MathPlugin());   // now the script has PI, sqrt, pow, and the rest
```

Or you let the loader find them on disk:

```csharp
var loaded = FishbonePluginLoader.LoadPlugins(
    FishbonePluginLoader.DefaultPluginsDirectory, config);
```

`DefaultPluginsDirectory` is `~/.fishbone/plugins`. Each plugin gets its own subfolder there, and every DLL in it is scanned for `IFishbonePlugin` types. Only plugins with a parameterless constructor are picked up this way (one that needs constructor arguments is meant to be built by the host and passed to `AddPlugin`). A DLL that fails to load is skipped rather than taking the whole loader down.

This is the path SpineIDE, SpineCLI and the DAP host all use, which is why [samples/edge_detect.fb](../samples/edge_detect.fb) works once you drop `Fishbone.Plugins.OpenCV` into that folder.

The three plugins in this repo are [Math](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.Math), [OpenCV](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.OpenCV) and [Halcon24111](https://github.com/cenfraGit/Fishbone/tree/main/plugins/Fishbone.Plugins.Halcon24111).

---

## 6. Putting it together

Expose your domain, parse once, run per item, optionally debuggable:

```csharp
using Fishbone;
using Fishbone.DebugAdapter;
using Fishbone.Plugins.Math;

public sealed class InspectionScripting
{
    private readonly FishboneProgram _program;

    public InspectionScripting(string scriptSource)
        => _program = FishboneProgram.FromSourceCode(scriptSource);   // parse once at load

    public InspectionResult Run(Image image, bool debug)
    {
        var config = new FishboneConfiguration()
            .AddPlugin(new MathPlugin()) // PI, sqrt, etc
            .AddBuiltIn("println", new Action<object>(Console.WriteLine))
            .AddValue("image", image) // visible in the debugger
            .AddType<Measurement>(); // constructable from the script

        FishboneEnvironment env = debug
            ? _program
                .RunDebuggableAsync(config, new FishboneDebugOptions
                {
                    OpenIde       = true,
                    AttachTimeout = TimeSpan.FromSeconds(10),
                })
                .GetAwaiter().GetResult()
                .Environment!
            : _program.Run(config); // no reparse

        return new InspectionResult(env.GetValue("result"));
    }
}
```

---

## API summary

| Call                                          | What it does                                                        |
|-----------------------------------------------|---------------------------------------------------------------------|
| `FishboneProgram.Run(source, config)`         | Parse and run in one shot, headless and synchronous                  |
| `FishboneProgram.FromSourceCode(source)`      | Build an immutable, reusable program from source text                |
| `FishboneProgram.FromFile(path)`              | The same, reading the file and using its name as the display name    |
| `program.Run(config, debugger, ct)`           | Execute a parsed program. Every argument is optional                 |
| `program.SourceIdentity`                      | SHA256 of the source, for spotting a stale cached program            |
| `program.RunDebuggableAsync(config, options)` | Run with an optional debugger or IDE (lives in `Fishbone.DebugAdapter`) |
| `FishboneDebugOptions`                        | Debug-only options (in `Fishbone.DebugAdapter`)                      |
| `FishboneRunResult`                           | What a debuggable run gives back (in `Fishbone.DebugAdapter`)        |
| `config.AddValue(name, obj)`                  | Seed a script variable, visible in the debugger                      |
| `config.AddBuiltIn(name, obj)`                | Bind a built-in function, service or constant                        |
| `config.AddType<T>()`                         | Make a .NET type constructable from a script                         |
| `config.AddTypeConverter(type, to, from)`     | Teach the interop path about a type it can't convert on its own      |
| `config.AddPlugin(plugin)`                    | Load an `IFishbonePlugin` into the config                            |
| `config.Clone()`                              | An independent copy of a config                                      |
| `FishbonePluginLoader.LoadPlugins(dir, cfg)`  | Scan a folder for plugin DLLs and register what it finds             |
| `env.GetValue(name)` / `env.TryGetValue(...)` | Pull a value out after the run                                       |
