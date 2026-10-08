using System.Runtime.InteropServices;
using Fishbone.Debugging;

namespace Fishbone.Plugins.Halcon24111;

/// <summary>
/// Turns the channel planes HALCON hands out (from get_image_pointer1 and get_image_pointer3) into
/// a <see cref="FishboneImage"/>. Kept apart from HALCON itself so it can be tested without it.
/// </summary>
public static class HalconImageConversion
{
    /// <summary>
    /// One plane is a gray image and three are RGB. A "byte" image is copied as is, and the other
    /// pixel types ("uint2", "int2", "int4", "real") are scaled with <see cref="FishboneImage.FromValues"/>.
    /// Returns null for a pixel type it can't show.
    /// </summary>
    public static FishboneImage? FromPlanes(string pixelType, int width, int height, IReadOnlyList<IntPtr> planes)
    {
        if (planes.Count is not (1 or 3))
            return null;

        int size = width * height;
        int channels = planes.Count;

        if (pixelType == "byte")
        {
            var pixels = new byte[size * channels];
            var plane = new byte[size];
            for (int c = 0; c < channels; c++)
            {
                Marshal.Copy(planes[c], plane, 0, size);
                for (int i = 0; i < size; i++)
                    pixels[i * channels + c] = plane[i];
            }
            return new FishboneImage(width, height, channels, pixels);
        }

        Func<IntPtr, double[]>? read = pixelType switch
        {
            "uint2" => pointer => Read<short>(pointer, size, value => (ushort)value),
            "int2" => pointer => Read<short>(pointer, size, value => value),
            "int4" => pointer => Read<int>(pointer, size, value => value),
            "real" => pointer => Read<float>(pointer, size, value => value),
            _ => null
        };
        if (read is null)
            return null;

        var values = new double[size * channels];
        for (int c = 0; c < channels; c++)
        {
            double[] plane = read(planes[c]);
            for (int i = 0; i < size; i++)
                values[i * channels + c] = plane[i];
        }
        return FishboneImage.FromValues(width, height, channels, values);
    }

    // Marshal.Copy has no ushort overload, so uint2 is read as short and reinterpreted
    private static double[] Read<T>(IntPtr pointer, int size, Func<T, double> toDouble) where T : struct
    {
        var raw = new T[size];
        switch (raw)
        {
            case short[] shorts:
                Marshal.Copy(pointer, shorts, 0, size);
                break;
            case int[] ints:
                Marshal.Copy(pointer, ints, 0, size);
                break;
            case float[] floats:
                Marshal.Copy(pointer, floats, 0, size);
                break;
        }
        return Array.ConvertAll(raw, value => toDouble(value));
    }
}
