using Fishbone.Debugging;
using HalconDotNet;

namespace Fishbone.Plugins.Halcon24111;

/// <summary>
/// Shows a HALCON image in the debugger. Images reach scripts as <see cref="HObject"/>, which can
/// also hold regions or contours, so only objects of class "image" count.
/// </summary>
public static class HalconVisualizer
{
    public static void Register(FishboneConfiguration config) =>
        config.AddVisualizer<HObject>(ToImage, CanShow);

    public static bool CanShow(HObject value)
    {
        if (!value.IsInitialized())
            return false;
        HOperatorSet.CountObj(value, out HTuple count);
        if (count.I < 1)
            return false;
        HOperatorSet.GetObjClass(value, out HTuple objectClass);
        return objectClass.Length > 0 && objectClass[0].S == "image";
    }

    /// <summary>The first image of the object. Three channels show as RGB, and any other count shows the first channel.</summary>
    public static FishboneImage? ToImage(HObject value)
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
}
