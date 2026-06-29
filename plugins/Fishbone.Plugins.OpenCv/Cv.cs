#pragma warning disable IDE1006 // Naming Styles

using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv;

public sealed class Cv
{
    public Mat imread(string path) => Cv2.ImRead(path, ImreadModes.Color);
    public Mat imread(string path, ImreadModes mode) => Cv2.ImRead(path, mode);
    public bool imwrite(string path, Mat image) => Cv2.ImWrite(path, image);

    public Mat toGray(Mat src)
    {
        var dst = new Mat();
        Cv2.CvtColor(src, dst, ColorConversionCodes.BGR2GRAY);
        return dst;
    }

    public Mat gaussianBlur(Mat src, int kernelSize)
    {
        var dst = new Mat();
        Cv2.GaussianBlur(src, dst, new Size(kernelSize, kernelSize), 0);
        return dst;
    }

    public Mat medianBlur(Mat src, int kernelSize)
    {
        var dst = new Mat();
        Cv2.MedianBlur(src, dst, kernelSize);
        return dst;
    }

    public Mat canny(Mat src, double threshold1, double threshold2)
    {
        var dst = new Mat();
        Cv2.Canny(src, dst, threshold1, threshold2);
        return dst;
    }

    public Mat threshold(Mat src, double thresh, double maxValue)
    {
        var dst = new Mat();
        Cv2.Threshold(src, dst, thresh, maxValue, ThresholdTypes.Binary);
        return dst;
    }

    public Mat adaptiveThreshold(Mat src, double maxValue, int blockSize, double c)
    {
        var dst = new Mat();
        Cv2.AdaptiveThreshold(src, dst, maxValue, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, blockSize, c);
        return dst;
    }

    public Mat resize(Mat src, int width, int height)
    {
        var dst = new Mat();
        Cv2.Resize(src, dst, new Size(width, height));
        return dst;
    }

    public Mat flip(Mat src, int flipCode)
    {
        var mode = flipCode == 0 ? FlipMode.X : flipCode > 0 ? FlipMode.Y : FlipMode.XY;
        var dst = new Mat();
        Cv2.Flip(src, dst, mode);
        return dst;
    }

    public Mat rotate90(Mat src)
    {
        var dst = new Mat();
        Cv2.Rotate(src, dst, RotateFlags.Rotate90Clockwise);
        return dst;
    }

    public Mat dilate(Mat src, int kernelSize)
    {
        using var element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
        var dst = new Mat();
        Cv2.Dilate(src, dst, element);
        return dst;
    }

    public Mat erode(Mat src, int kernelSize)
    {
        using var element = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
        var dst = new Mat();
        Cv2.Erode(src, dst, element);
        return dst;
    }

    public Mat equalizeHist(Mat src)
    {
        var dst = new Mat();
        Cv2.EqualizeHist(src, dst);
        return dst;
    }
}
#pragma warning restore IDE1006 // Naming Styles