using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleToSvg.Svg;

/// <summary>Managed boundary for the repository-owned resvg wrapper.</summary>
internal static class ResvgNative
{
    private const string LibraryName = "console2svg_resvg";

    static ResvgNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(ResvgNative).Assembly, ResolveNativeLibrary);
    }

    /// <summary>
    /// Explicitly probes the bundled asset directories (see <see
    /// cref="AppPaths"/>) before falling back to the default probing order.
    /// The default OS/CLR search alone is not enough: WinGet's portable
    /// installer runs console2svg through a symlink, and the implicit
    /// "next to the executable" search then looks in the symlink's
    /// directory instead of the directory the DLL actually ships in.
    /// </summary>
    private static IntPtr ResolveNativeLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath
    )
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var fileName = GetPlatformLibraryFileName(libraryName);
        foreach (var dir in AppPaths.GetBundledAssetDirectories())
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallback)
            ? fallback
            : IntPtr.Zero;
    }

    private static string GetPlatformLibraryFileName(string libraryName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"{libraryName}.dll";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"lib{libraryName}.dylib";
        }
        return $"lib{libraryName}.so";
    }

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int c2s_resvg_warm_system_fonts();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int c2s_resvg_render_png(
        byte[] svg,
        nuint svgLength,
        int width,
        int height,
        out IntPtr pngBuffer,
        out nuint pngLength
    );

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void c2s_resvg_free_buffer(IntPtr buffer, nuint length);

    /// <summary>Loads the process-wide system font database once.</summary>
    public static void WarmSystemFonts()
    {
        ThrowForStatus(c2s_resvg_warm_system_fonts());
    }

    /// <summary>Renders the full SVG viewport to PNG.</summary>
    public static byte[] RenderToPng(string svg, int? width, int? height)
    {
        if (string.IsNullOrEmpty(svg))
        {
            throw new ArgumentException("SVG must not be null or empty.", nameof(svg));
        }

        var byteCount = Encoding.UTF8.GetByteCount(svg);
        var svgBytes = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var bytesWritten = Encoding.UTF8.GetBytes(svg.AsSpan(), svgBytes.AsSpan(0, byteCount));
            var status = c2s_resvg_render_png(
                svgBytes,
                (nuint)bytesWritten,
                width ?? -1,
                height ?? -1,
                out var pngBuffer,
                out var pngLength
            );
            ThrowForStatus(status);

            try
            {
                var length = checked((int)pngLength);
                var png = new byte[length];
                Marshal.Copy(pngBuffer, png, 0, length);
                return png;
            }
            finally
            {
                if (pngBuffer != IntPtr.Zero)
                {
                    c2s_resvg_free_buffer(pngBuffer, pngLength);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(svgBytes);
        }
    }

    private static void ThrowForStatus(int status)
    {
        if (status == 0)
        {
            return;
        }

        throw status switch
        {
            1 => new InvalidOperationException("resvg could not parse the SVG."),
            2 => new InvalidOperationException("resvg could not encode the PNG."),
            3 => new InvalidOperationException("resvg could not render the SVG."),
            4 => new OutOfMemoryException("resvg could not allocate the PNG buffer."),
            _ => new InvalidOperationException($"resvg failed with native status {status}."),
        };
    }
}
