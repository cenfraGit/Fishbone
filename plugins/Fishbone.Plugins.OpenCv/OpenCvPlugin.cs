using Fishbone.Engine;
using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Fishbone.Plugins.OpenCv;

/// <summary>
/// Exposes a curated set of OpenCV image operations to Fishbone scripts under the <c>cv</c>
/// built-in.
/// </summary>
public sealed class OpenCvPlugin : IFishbonePlugin
{
    private static int _nativeResolverRegistered;
    private static AssemblyDependencyResolver? _dependencyResolver;

    public void Register(FishboneConfiguration config)
    {
        EnsureNativeResolver();

        // scripts call cv.method()
        config.AddBuiltIn("cv", new Cv());
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