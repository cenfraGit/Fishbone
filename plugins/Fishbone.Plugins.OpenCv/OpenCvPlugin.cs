using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Fishbone.Engine;
using Fishbone.Interpreter;
using OpenCvSharp;

namespace Fishbone.Plugins.OpenCv;

/// <summary>
/// Exposes OpenCV's <see cref="Cv2"/> operations to Fishbone by reflection. Each public static
/// method is bound under a <c>cv_</c>-prefixed snake-case name (for example <c>cv_cvt_color</c>,
/// <c>cv_gaussian_blur</c>, <c>cv_canny</c>) and called with native syntax. Because OpenCV writes its
/// output into a destination <c>Mat</c> passed in (rather than returning it), scripts allocate the
/// destination themselves and read it back afterwards:
///
/// <code>
/// let dst = Mat();
/// cv_cvt_color(src, dst, "BGR2GRAY");   // dst is filled in place
/// </code>
///
/// The wrapper-type conversions that make this work (<c>Mat</c> to InputArray/OutputArray, lists to
/// Size/Scalar/Point) are registered as Fishbone type converters; optional OpenCV parameters may be
/// omitted and take their defaults.
/// </summary>
public sealed partial class OpenCvPlugin : IFishbonePlugin
{
    private static int _nativeResolverRegistered;
    private static AssemblyDependencyResolver? _dependencyResolver;

    public void Register(FishboneConfiguration config)
    {
        EnsureNativeResolver();

        // construct Mats from scripts: 'let dst = Mat();'
        config.AddType<Mat>();

        RegisterConverters(config);
        RegisterCv2Operations(config);
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

    private static void RegisterCv2Operations(FishboneConfiguration config)
    {
        var methods = typeof(Cv2)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(IsExposable);

        // group overloads under one name so a call resolves across all of them; prefix with "cv_"
        // to namespace the operations and avoid colliding with other plugins' built-ins
        foreach (var overloads in methods.GroupBy(method => "cv_" + ToSnakeCase(method.Name)))
            config.AddBuiltIn(overloads.Key, new BoundMethod(target: null!, overloads.ToArray()));
    }

    /// <summary>
    /// Filters out methods the interop path cannot invoke: generic definitions, operators/accessors,
    /// and anything taking a pointer. Methods whose parameters reference OpenCV types without a
    /// registered converter are still bound.
    /// </summary>
    private static bool IsExposable(MethodInfo method)
    {
        if (method.IsSpecialName || method.IsGenericMethodDefinition)
            return false;

        foreach (var parameter in method.GetParameters())
        {
            var type = parameter.ParameterType;
            if (type.IsPointer || (type.IsByRef && type.GetElementType()!.IsPointer))
                return false;
        }

        return true;
    }

    public static string ToSnakeCase(string pascal)
    {
        var result = AcronymPattern().Replace(pascal, "$1_$2");
        result = WordBoundaryPattern().Replace(result, "$1_$2");
        return result.ToLowerInvariant();
    }

    [GeneratedRegex(@"([A-Z]+)([A-Z][a-z])")]
    private static partial Regex AcronymPattern();

    [GeneratedRegex(@"([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundaryPattern();

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