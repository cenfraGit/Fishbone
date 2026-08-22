using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Fishbone.Engine;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv;

/// <summary>
/// Exposes OpenCV to Fishbone by registering <see cref="Cv2"/> as <c>cv</c>, so every public
/// static member is reachable through the dot operator under its .NET name (for example
/// <c>cv.CvtColor</c>, <c>cv.GaussianBlur</c>, <c>cv.Canny</c>, and constants like
/// <c>cv.FILLED</c>). Because OpenCV writes its output into a destination <c>Mat</c> passed in
/// (rather than returning it), scripts allocate the destination themselves and read it back
/// afterwards:
///
/// <code>
/// let dst = Mat();
/// cv.CvtColor(src, dst, "BGR2GRAY");   // dst is filled in place
/// </code>
///
/// <see cref="Mat"/> is registered too, which makes both <c>Mat()</c> construction and its static
/// factories (<c>Mat.Zeros</c>, <c>Mat.Ones</c>, <c>Mat.Eye</c>) available. The wrapper-type
/// conversions that make all this work (<c>Mat</c> to InputArray/OutputArray, lists to
/// Size/Scalar/Point) are registered as Fishbone type converters; optional OpenCV parameters may be
/// omitted and take their defaults.
/// </summary>
public sealed class OpenCvPlugin : IFishbonePlugin
{
    private static int _nativeResolverRegistered;
    private static AssemblyDependencyResolver? _dependencyResolver;

    public void Register(FishboneConfiguration config)
    {
        EnsureNativeResolver();

        // construct Mats from scripts ('let dst = Mat();') and reach Mat's static factories
        config.AddType<Mat>();

        // MatType is a struct whose depth/channel combinations are static fields, so registering
        // it lets scripts name them: 'Mat.Zeros(4, 6, MatType.CV_8UC1);'
        config.AddType<MatType>();

        // every Cv2 static member under one name: 'cv.GaussianBlur(src, dst, [5, 5], 0);'
        config.AddType(typeof(Cv2), "cv");

        RegisterConverters(config);
    }

    private static void RegisterConverters(FishboneConfiguration config)
    {
        config.AddTypeConverter(typeof(InputArray), OpenCvConverters.ToInputArray);
        config.AddTypeConverter(typeof(OutputArray), OpenCvConverters.ToOutputArray);
        config.AddTypeConverter(typeof(InputOutputArray), OpenCvConverters.ToInputOutputArray);
        config.AddTypeConverter(typeof(Size), OpenCvConverters.ToSize);
        config.AddTypeConverter(typeof(Point), OpenCvConverters.ToPoint);
        config.AddTypeConverter(typeof(Scalar), OpenCvConverters.ToScalar);
    }

    private static void EnsureNativeResolver()
    {
        if (Interlocked.Exchange(ref _nativeResolverRegistered, 1) == 1)
            return;

        var pluginPath = typeof(OpenCvPlugin).Assembly.Location;
        if (string.IsNullOrEmpty(pluginPath))
            return;

        _dependencyResolver = new AssemblyDependencyResolver(pluginPath);

        try
        {
            NativeLibrary.SetDllImportResolver(typeof(Cv2).Assembly, (libraryName, _, _) =>
            {
                var resolved = _dependencyResolver.ResolveUnmanagedDllToPath(libraryName);
                if (resolved is not null && NativeLibrary.TryLoad(resolved, out var handle))
                    return handle;
                return IntPtr.Zero;
            });
        }
        catch (InvalidOperationException)
        {
        }
    }
}