using Fishbone.Debugging;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

/// <summary>
/// Shows a HALCON object in the debugger. Images, regions and XLD contours all reach scripts as
/// <see cref="HObject"/>: an image shows as pixels, and regions and contours as shapes the
/// debugger draws, on their own or over an image it already shows.
/// </summary>
public static class HalconVisualizer
{
    public static void Register(FishboneConfiguration config) =>
        config.AddVisualizer<HObject>(ToImage, CanShow, Images);

    // xld_poly is what gen_polygons_xld makes. the other xld kinds, like parallels, are left out
    private static readonly string[] Classes = ["image", "region", "xld_cont", "xld_poly"];

    public static bool CanShow(HObject value)
    {
        if (!value.IsInitialized())
            return false;
        HOperatorSet.CountObj(value, out HTuple count);
        if (count.I < 1)
            return false;
        HOperatorSet.GetObjClass(value, out HTuple objectClass);
        return objectClass.Length > 0 && objectClass.SArr.All(Classes.Contains);
    }

    /// <summary>
    /// The first image of the object, or when it's regions and contours, all of them as shapes.
    /// Three channels show as RGB, and any other count shows the first channel.
    /// </summary>
    public static FishboneImage? ToImage(HObject value)
    {
        HOperatorSet.GetObjClass(value, out HTuple objectClass);
        string[] classes = objectClass.SArr;
        return classes[0] == "image" ? FirstImage(value) : Shapes(value, classes);
    }

    /// <summary>
    /// One child per image when the object holds several, named [1] to [n] like select_obj counts.
    /// Regions and contours already show all together, so they have none.
    /// </summary>
    public static IReadOnlyList<(string Name, object? Value)>? Images(HObject value)
    {
        HOperatorSet.CountObj(value, out HTuple count);
        if (count.I < 2)
            return null;
        HOperatorSet.GetObjClass(value, out HTuple objectClass);
        if (!objectClass.SArr.All(name => name == "image"))
            return null;
        var images = new List<(string, object?)>(count.I);
        for (int i = 1; i <= count.I; i++)
        {
            HOperatorSet.SelectObj(value, out HObject single, i);
            images.Add(($"[{i}]", single));
        }
        return images;
    }

    private static FishboneImage? FirstImage(HObject value)
    {
        HOperatorSet.SelectObj(value, out HObject first, 1);
        try
        {
            HOperatorSet.CountChannels(first, out HTuple channels);
            HTuple type, width, height;
            IntPtr[] planes;
            if (channels.I == 3)
            {
                HOperatorSet.GetImagePointer3(first, out HTuple red, out HTuple green, out HTuple blue, out type, out width, out height);
                planes = [new IntPtr(red.L), new IntPtr(green.L), new IntPtr(blue.L)];
            }
            else
            {
                HOperatorSet.GetImagePointer1(first, out HTuple pointer, out type, out width, out height);
                planes = [new IntPtr(pointer.L)];
            }
            return HalconImageConversion.FromPlanes(type.S, width.I, height.I, planes);
        }
        finally
        {
            first.Dispose();
        }
    }

    // the runs and points HALCON reads out one object at a time
    private static FishboneImage Shapes(HObject value, string[] classes)
    {
        var regions = new List<FishboneRegion>();
        var contours = new List<FishboneContour>();
        for (int i = 0; i < classes.Length; i++)
        {
            HOperatorSet.SelectObj(value, out HObject single, i + 1);
            try
            {
                switch (classes[i])
                {
                    case "region":
                        HOperatorSet.GetRegionRuns(single, out HTuple rows, out HTuple columnStarts, out HTuple columnEnds);
                        regions.Add(new FishboneRegion(Ints(rows), Ints(columnStarts), Ints(columnEnds)));
                        break;
                    case "xld_cont":
                        HOperatorSet.GetContourXld(single, out HTuple contourRows, out HTuple contourColumns);
                        contours.Add(new FishboneContour(Doubles(contourRows), Doubles(contourColumns)));
                        break;
                    case "xld_poly":
                        HOperatorSet.GetPolygonXld(single, out HTuple polygonRows, out HTuple polygonColumns, out _, out _);
                        contours.Add(new FishboneContour(Doubles(polygonRows), Doubles(polygonColumns)));
                        break;
                }
            }
            finally
            {
                single.Dispose();
            }
        }
        return FishboneImage.FromShapes(regions, contours);
    }

    // an empty region or contour gives empty tuples, which have no number type to convert from
    private static int[] Ints(HTuple tuple) => tuple.Length == 0 ? [] : tuple.ToIArr();
    private static double[] Doubles(HTuple tuple) => tuple.Length == 0 ? [] : tuple.ToDArr();
}
