using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConsoleToSvg.Cli;

namespace ConsoleToSvg;

internal static partial class Program
{
    private static async Task<int> RunConfigAsync(string[] args, AppOptions options)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("-", StringComparison.Ordinal)) continue;
            if (token == "--file") { i++; continue; }
            var split = token.IndexOf('=');
            var key = split > 0 ? token[..split] : token;
            if (key is "--help" or "--version") continue;
            object value = true;
            if (split > 0) value = token[(split + 1)..];
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)) value = args[++i];
            values[key.TrimStart('-')] = value;
        }
        if (values.Count == 0)
        {
            await Console.Error.WriteLineAsync("config requires at least one option to save.");
            return 1;
        }
        var path = options.ConfigPath ?? "console2svg.config.json";
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var lines = new List<string> { "{" };
        foreach (var pair in values)
        {
            var value = pair.Value is bool ? "true" : "\"" + pair.Value!.ToString()!.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
            lines.Add("  \"" + pair.Key + "\": " + value + ",");
        }
        lines[^1] = lines[^1].TrimEnd(',');
        lines.Add("}");
        await File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Saved configuration: {path}");
        return 0;
    }
}
