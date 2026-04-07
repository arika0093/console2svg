using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ResvgSharp;

namespace ConsoleToSvg.Raster;

internal static class RasterImageRenderer
{
    internal static byte[] RenderPng(string svg, string? resourcesDirectory)
    {
        var options = new ResvgOptions
        {
            ExportAreaPage = true,
            ExportAreaDrawing = false,
            ResourcesDir = string.IsNullOrWhiteSpace(resourcesDirectory)
                ? null
                : Path.GetFullPath(resourcesDirectory),
        };

        try
        {
            return Resvg.RenderToPng(svg, options);
        }
        catch (DllNotFoundException ex)
        {
            throw CreateNativeLoadException(ex);
        }
        catch (BadImageFormatException ex)
        {
            throw CreateNativeLoadException(ex);
        }
    }

    internal static async Task WritePngFileAsync(
        string svgPath,
        string pngPath,
        string? resourcesDirectory,
        CancellationToken cancellationToken
    )
    {
        var effectiveResourcesDirectory =
            string.IsNullOrWhiteSpace(resourcesDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(svgPath))
                : resourcesDirectory;

        var svg = await File.ReadAllTextAsync(svgPath, cancellationToken).ConfigureAwait(false);
        var pngBytes = RenderPng(svg, effectiveResourcesDirectory);
        await File.WriteAllBytesAsync(pngPath, pngBytes, cancellationToken).ConfigureAwait(false);
    }

    private static InvalidOperationException CreateNativeLoadException(Exception innerException)
    {
        return new InvalidOperationException(
            $"Failed to load the ResvgSharp native library for {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). "
                + "Ensure the matching resvg_wrapper runtime asset is present in the build or publish output.",
            innerException
        );
    }
}
