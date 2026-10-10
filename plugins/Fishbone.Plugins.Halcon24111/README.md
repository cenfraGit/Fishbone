# Fishbone.Plugins.Halcon24111

Calls MVTec HALCON 24.11 from Fishbone scripts. It needs HALCON installed.

- `HalconOperatorPlugin` registers every HALCON operator under its HDevelop name, like `read_image` or `affine_trans_point_2d`.
- `HalconProcedurePlugin` registers the HDevelop procedures (`.hdvp`) in one or more folders. Registering again doesn't re-read them, so call `HalconProcedurePlugin.Reload(folders)` after the files change.
- Both show images, regions and XLD contours in the debugger.

A call reads like in HDevelop, in the same order, with `out` on the outputs:

```
read_image(out image, "fabrik");
threshold(image, out region, 128, 255);
count_obj(region, out n);
```

## Tuples

A control output is a HALCON tuple, and it comes back as an ordinary script value. What you get depends on how many elements it has:

| Elements | Script value |
|---|---|
| none | `null` |
| one | the value itself: a `long`, a `double` or a `string`. A handle, like a dictionary, stays an `HTuple` |
| two or more | a list of them, with each handle a one-element `HTuple` |

So the same line gives a different kind of value depending on the image. With one region, `area_center(regions, out areas, out rows, out cols)` makes `areas` a number, and `foreach (a in areas)` fails, since a number isn't something to loop over. With none, `areas` is `null`.

This is deliberate. The plugin doesn't guess, and a script handles it the way HDevelop or C# code does: take the count from the objects, and pick each element with `tuple_select`. That works for none, one or many:

```
area_center(regions, out areas, out rows, out cols);
count_obj(regions, out n);
for (i in 0, n) {
    tuple_select(areas, i, out area);
    println(area);
}
```

Going the other way, a script list becomes a tuple, and `[]` is the empty tuple.
