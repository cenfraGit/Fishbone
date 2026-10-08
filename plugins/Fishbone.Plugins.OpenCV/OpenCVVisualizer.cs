using System.Runtime.InteropServices;
using Fishbone.Debugging;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCV;

/// <summary>Shows a <see cref="Mat"/> in the debugger.</summary>
public static class OpenCVVisualizer
{
    /// <summary>A non-empty, two-dimensional Mat with 1, 3 or 4 channels.</summary>
    public static bool CanShow(Mat mat) =>
        !mat.IsDisposed && !mat.Empty() && mat.Dims == 2 && mat.Channels() is 1 or 3 or 4;

    /// <summary>
    /// The Mat's pixels, with BGR turned into RGB. An 8-bit Mat is copied as is, and any other depth
    /// is scaled with <see cref="FishboneImage.FromValues"/>, the same rule as HALCON images.
    /// </summary>
    public static FishboneImage? ToImage(Mat mat)
    {
        if (!CanShow(mat))
            return null;

        int channels = mat.Channels();
        bool isByte = mat.Depth() == MatType.CV_8U;

        // cvtColor only takes 8-bit, 16-bit and float, so other depths go through float first
        using var source = new Mat();
        if (isByte)
            mat.CopyTo(source);
        else
            mat.ConvertTo(source, MatType.CV_32FC(channels));

        if (channels > 1)
            Cv2.CvtColor(source, source, channels == 3 ? ColorConversionCodes.BGR2RGB : ColorConversionCodes.BGRA2RGBA);

        // a copy is continuous, so the rows sit back to back with no padding
        int count = mat.Rows * mat.Cols * channels;
        if (isByte)
        {
            var pixels = new byte[count];
            Marshal.Copy(source.Data, pixels, 0, count);
            return new FishboneImage(mat.Cols, mat.Rows, channels, pixels);
        }

        var values = new float[count];
        Marshal.Copy(source.Data, values, 0, count);
        return FishboneImage.FromValues(mat.Cols, mat.Rows, channels, Array.ConvertAll(values, value => (double)value));
    }
}
