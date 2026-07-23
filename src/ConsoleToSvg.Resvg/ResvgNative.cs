using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleToSvg.Svg;

/// <summary>Managed boundary for the repository-owned resvg wrapper.</summary>
internal static class ResvgNative
{
    private const string LibraryName = "console2svg_resvg";

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

        var svgBytes = Encoding.UTF8.GetBytes(svg);
        var status = c2s_resvg_render_png(
            svgBytes,
            (nuint)svgBytes.Length,
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
