# Changelog

## 0.1.0-alpha.2

This release changes what some existing scripts and hosts do. Everything below is measured against the published 0.1.0-alpha.1. From this release on, syntax follows the [syntax stability rules](README.md#syntax-stability).

### Breaking: language

| Script | alpha.1 | alpha.2 |
|---|---|---|
| `let d = {};` | an empty dictionary | parse error. Write `{:}` |
| `3.7 as int`, `-2.7 as int` | `4`, `-3` (rounded) | `3`, `-2` (truncated toward zero, like C#) |
| `"42" as int`, `"12.5" as double`, `42 as string` | `42`, `12.5`, `"42"` | `null`. `as` doesn't parse text, use `int.Parse("42")` |
| `2147483647 + 1` | `-2147483648` (wrapped around) | runtime error. Use `as long` for bigger values |
| `for (i in 0, 3)` | `i` is a `double` | `i` is an `int` (a `long` or `double` only when the range is) |
| `for (i in 0, 1, 0.1)` | runs 11 times | runs 10 times. Each value is `start + k * step` |
| `for (i in 0, 10, 0)`, `for (i in 0, 10, -1)` | never ends | runtime error |
| `break` or `continue` outside a loop, `return` outside a function | runs, or fails when reached | parse error |
| `let int = 5;` (any C# type keyword as a name) | allowed | parse error. Also for functions, parameters, loop and `catch` variables, and `out` targets |

Arguments to .NET calls convert strictly now. A conversion that loses or invents information fails with an error that names the function, the argument and both types:

| Argument | Parameter | alpha.1 | alpha.2 |
|---|---|---|---|
| `2.5` | `int` | `2` | error |
| `2.0` | `int` | `2` | `2` (a whole double still converts) |
| `"3"` | `int` | `3` | error |
| `5` | `string` | `"5"` | error |
| `"Monday"` | an enum | `Monday` | error. An integer still converts |
| `1` | `bool` | `true` | error |
| `null` | `int` (any value type) | `0` | error |
| `[1, 2]` | `List<int>`, `int[]` | error | converts |

Equality now compares every .NET number type by value, so a `float` or `decimal` from .NET equals the same `int`, and `"A"[0] == 65` is `true`. alpha.1 only did that for `int`, `long` and `double`.

### Breaking: hosts and plugins

- `FishboneConfiguration.AddBuiltIn`, `AddValue` and `AddType` throw `ArgumentException` for a reserved type name like `int` or `string`.
- `HalconProcedurePlugin(string)` became `HalconProcedurePlugin(params string[])`. Source compatible, but code compiled against alpha.1 has to be rebuilt.
- `HalconProcedurePlugin.Register` throws `FishboneConfigurationException` for a missing folder, and for a procedure named like an existing built-in or found in two folders. Its `Names` lists the clashing names.
- HALCON operator names come from HALCON itself. 152 operators with a digit group gained an underscore, for example `find_data_code2d` is now `find_data_code_2d`, `read_object_model3d` is now `read_object_model_3d` and `affine_trans_point2d` is now `affine_trans_point_2d`.
- `FishboneDebugAdapterSession` and `DebugSnapshotHandles` take an optional `FishboneConfiguration` in their constructors. `BreakpointCoordinator.RemoveBreakpoint(int)` and `LastResumeWasSuccessful` were removed.
- SpineIDE is a native Win32 app now, without Avalonia, and Windows only. There's no `Fishbone.SpineIDE.linux-x64` package for this release; a Linux version is planned.
- In SpineIDE, a Continue that reaches the end of the script ends the debug session. Only stepping off the last line still stops there with the final values.

### Added

**Language**
- Member assignment: `obj.Value = x`, `box.Count += 1`.
- Static members and enum values on registered types and C# type names: `int.Parse("42")`, `DayOfWeek.Monday`.
- Calling a delegate stored in a variable directly.
- Calling a registered struct with no arguments gives its default value, like `new Point()` in C#.
- `as` works with every C# type name (`as byte`, `as long`, `as char`...), and gives `null` when the value is out of range. alpha.1 only had `int`, `double`, `string` and `bool`.
- Array casts: `[1, 2] as int[]`.

**Hosts**
- `FishboneConfiguration.Describe()` lists everything a configuration puts into scripts.
- `FishboneAnalysis.Analyze(...)` gives diagnostics, completions and signature help without running the script.
- `FishboneExpression.Evaluate` evaluates one expression against an environment.
- `FishboneConfigurationException` for configuration setup errors.
- Packages target both `net8.0` and `net10.0`. SpineIDE and `fishbone-dap` run on .NET 8, or on the newest installed runtime when 8 isn't there.

**Debugger**
- `AddVisualizer` shows a .NET type as an image while paused, with regions and contours drawn over it. Its optional `children` splits a value that holds several images into one child per image.
- The debug adapter answers `evaluate` and `completions`, so watches work in any DAP client.
- `RunDebuggableAsync` with `OpenIde` reuses an idle SpineIDE window instead of opening a new one.

**Plugins**
- `HalconProcedurePlugin` takes several folders, and a procedure can call procedures from any of them.
- `HalconProcedurePlugin.Reload(...)` picks up procedure files that were added or edited on disk. Registering doesn't reload them by itself.
- The HALCON plugin shows images, regions and XLD contours in the debugger, and the OpenCV plugin shows `Mat`s.

**SpineIDE**
- Watches with completion and parameter tips.
- An image preview with zoom, pan and full screen. Checked variables stack over it: one image with regions and contours on top. Images inside lists, dictionaries and image arrays can be checked too.
- Completion, parameter tips and diagnostics in the editor.

### Fixed

- HALCON handles, like dictionaries, survive a script. Two or more handles collected with `tuple_concat` came back as strings, so the next operator given them failed with "wrong type of control parameter". Each handle in such a list is now a one-element `HTuple`.
